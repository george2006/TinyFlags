using System.Net;
using System.Text;
using System.Text.Json;

namespace TinyFlags.Tests;

public sealed class FeatureSnapshotReaderTests
{
    private const string EnvironmentText = "550e8400-e29b-41d4-a716-446655440000";
    private const string ETag = "W/\"" + EnvironmentText + ":12\"";

    [Fact]
    public async Task Reads_typed_values_and_keeps_them_after_the_response_is_disposed()
    {
        var response = Response("""
            {"revision":12,"values":[
                {"key":"Enabled","kind":"Boolean","value":true},
                {"key":"Label","kind":"String","value":"Buy \"now\""},
                {"key":"OtherAssembly.Empty","kind":"String","value":""},
                {"key":"enabled","kind":"Boolean","value":false}]}
            """);
        var catalog = new[] { FeatureDefinition.Boolean("Enabled", false), FeatureDefinition.String("Label", "Local") };

        var snapshot = await ReadAsync(response, catalog);
        response.Dispose();

        Assert.Equal(Guid.Parse(EnvironmentText), snapshot.EnvironmentId);
        Assert.Equal(12, snapshot.Revision);
        Assert.Equal(ETag, snapshot.EntityTag);
        Assert.True(Assert.IsType<bool>(snapshot.Values["Enabled"]));
        Assert.False(Assert.IsType<bool>(snapshot.Values["enabled"]));
        Assert.Equal("Buy \"now\"", snapshot.Values["Label"]);
        Assert.Equal("", snapshot.Values["OtherAssembly.Empty"]);
        var dictionary = Assert.IsAssignableFrom<IDictionary<string, object>>(snapshot.Values);
        Assert.Throws<NotSupportedException>(() => dictionary.Add("Mutation", true));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public async Task Empty_snapshots_preserve_the_exact_server_revision(long revision)
    {
        using var response = Response($$"""{"revision":{{revision}},"values":[]} """,
            $"W/\"{EnvironmentText}:{revision}\"");

        var snapshot = await ReadAsync(response, [FeatureDefinition.Boolean("Missing", true)]);

        Assert.Equal(revision, snapshot.Revision);
        Assert.Empty(snapshot.Values);
    }

    [Fact]
    public async Task Snapshot_can_contain_more_flags_than_one_registration_batch()
    {
        var entries = Enumerable.Range(0, 1001).Select(index => new { key = "Flag" + index, kind = "Boolean", value = true });
        using var response = Response(JsonSerializer.Serialize(new { revision = 12, values = entries }));

        var snapshot = await ReadAsync(response, []);

        Assert.Equal(1001, snapshot.Values.Count);
        Assert.True(Assert.IsType<bool>(snapshot.Values["Flag1000"]));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"revision\":-1,\"values\":[]}")]
    [InlineData("{\"revision\":12.5,\"values\":[]}")]
    [InlineData("{\"revision\":\"12\",\"values\":[]}")]
    [InlineData("{\"revision\":9223372036854775808,\"values\":[]}")]
    [InlineData("{\"revision\":12,\"revision\":12,\"values\":[]}")]
    [InlineData("{\"revision\":12,\"values\":null}")]
    [InlineData("{\"revision\":12,\"values\":{}}")]
    public async Task Rejects_invalid_envelopes(string json)
    {
        using var response = Response(json);

        await Assert.ThrowsAnyAsync<JsonException>(() => ReadAsync(response, []));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"key\":\" \",\"kind\":\"Boolean\",\"value\":true}")]
    [InlineData("{\"key\":1,\"kind\":\"Boolean\",\"value\":true}")]
    [InlineData("{\"key\":\"Bad\",\"kind\":\"Number\",\"value\":1}")]
    [InlineData("{\"key\":\"Bad\",\"kind\":0,\"value\":true}")]
    [InlineData("{\"key\":\"Bad\",\"kind\":\"Boolean\",\"value\":\"true\"}")]
    [InlineData("{\"key\":\"Bad\",\"kind\":\"Boolean\",\"value\":1}")]
    [InlineData("{\"key\":\"Bad\",\"kind\":\"String\",\"value\":null}")]
    [InlineData("{\"key\":\"Bad\",\"kind\":\"String\",\"value\":true}")]
    [InlineData("{\"key\":\"Bad\",\"kind\":\"String\"}")]
    [InlineData("{\"key\":\"Good\",\"kind\":\"Boolean\",\"value\":false}")]
    [InlineData("{\"key\":\"Bad\",\"kind\":\"Boolean\",\"value\":true,\"value\":false}")]
    public async Task Rejects_the_whole_snapshot_when_an_entry_after_a_valid_entry_is_invalid(string entry)
    {
        using var response = Response("""
            {"revision":12,"values":[{"key":"Good","kind":"Boolean","value":true},
            """ + entry + "]}");

        await Assert.ThrowsAnyAsync<JsonException>(() => ReadAsync(response, []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("*")]
    [InlineData("W/\"" + EnvironmentText + ":11\"")]
    [InlineData("W/\"" + EnvironmentText + ":012\"")]
    [InlineData("W/\"00000000-0000-0000-0000-000000000000:12\"")]
    [InlineData("W/\"not-an-environment:12\"")]
    [InlineData("W/\"" + EnvironmentText + ":12\", W/\"" + EnvironmentText + ":12\"")]
    public async Task Rejects_missing_invalid_or_mismatched_tags(string? tag)
    {
        using var response = Response("{\"revision\":12,\"values\":[]}", tag);

        await Assert.ThrowsAnyAsync<JsonException>(() => ReadAsync(response, []));
    }

    [Fact]
    public async Task Rejects_a_known_feature_changing_kind()
    {
        using var response = Response("""
            {"revision":12,"values":[{"key":"Enabled","kind":"String","value":"changed"}]}
            """);

        await Assert.ThrowsAsync<JsonException>(() => ReadAsync(response, [FeatureDefinition.Boolean("Enabled", false)]));
    }

    [Theory]
    [InlineData(304)]
    [InlineData(401)]
    [InlineData(503)]
    public async Task Non_snapshot_status_cannot_be_parsed_as_an_empty_snapshot(int status)
    {
        using var response = Response("{\"revision\":12,\"values\":[]}");
        response.StatusCode = (HttpStatusCode)status;

        await Assert.ThrowsAsync<JsonException>(() => ReadAsync(response, []));
    }

    [Fact]
    public async Task Rejects_non_json_content_and_honors_cancellation()
    {
        using var response = Response("{\"revision\":12,\"values\":[]}");
        response.Content.Headers.ContentType!.MediaType = "text/html";
        await Assert.ThrowsAsync<JsonException>(() => ReadAsync(response, []));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadAsync(response, [], cancellation.Token));
    }

    private static Task<FeatureSnapshot> ReadAsync(HttpResponseMessage response,
        IReadOnlyList<FeatureDefinition> catalog, CancellationToken ct = default)
        => new FeatureSnapshotReader(8 * 1024 * 1024).ReadAsync(response, catalog, ct);

    private static HttpResponseMessage Response(string json, string? tag = ETag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (tag is not null)
        {
            response.Headers.TryAddWithoutValidation("ETag", tag);
        }
        return response;
    }
}
