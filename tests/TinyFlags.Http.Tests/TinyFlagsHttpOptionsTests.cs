namespace TinyFlags.Tests;

public sealed class TinyFlagsHttpOptionsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_snapshot_limit_is_rejected_before_startup(int bytes)
    {
        var options = new TinyFlagsHttpOptions
        {
            Endpoint = new Uri("https://flags.example.com"), ApiKey = "test-key", MaxSnapshotBytes = bytes
        };

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => options.CreateSnapshot());

        Assert.Equal(nameof(TinyFlagsHttpOptions.MaxSnapshotBytes), error.ParamName);
    }

    [Fact]
    public void Snapshot_limit_is_copied_and_participates_in_configuration_equality()
    {
        var options = new TinyFlagsHttpOptions { Endpoint = new Uri("https://flags.example.com"), ApiKey = "test-key" };
        Assert.Equal(8 * 1024 * 1024, options.MaxSnapshotBytes);
        options.MaxSnapshotBytes = 1024;
        var snapshot = options.CreateSnapshot();
        Assert.True(snapshot.HasSameConfigurationAs(options.CreateSnapshot()));

        options.MaxSnapshotBytes = 2048;

        Assert.Equal(1024, snapshot.MaxSnapshotBytes);
        Assert.False(snapshot.HasSameConfigurationAs(options.CreateSnapshot()));
    }

    [Theory]
    [InlineData(0, 30000)]
    [InlineData(-1, 30000)]
    [InlineData(2147483648L, 2147483648L)]
    [InlineData(1000, 999)]
    [InlineData(1000, 2147483648L)]
    public void Invalid_retry_settings_are_rejected_before_startup(long initial, long maximum)
    {
        var options = new TinyFlagsHttpOptions
        {
            Endpoint = new Uri("https://flags.example.com"), ApiKey = "test-key",
            RetryDelay = TimeSpan.FromMilliseconds(initial), MaxRetryDelay = TimeSpan.FromMilliseconds(maximum)
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.CreateSnapshot());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(2147483648L)]
    public void Invalid_refresh_interval_is_rejected_before_startup(long milliseconds)
    {
        var options = new TinyFlagsHttpOptions
        {
            Endpoint = new Uri("https://flags.example.com"), ApiKey = "test-key",
            RefreshInterval = TimeSpan.FromMilliseconds(milliseconds)
        };

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => options.CreateSnapshot());

        Assert.Equal(nameof(TinyFlagsHttpOptions.RefreshInterval), error.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("relative/path")]
    [InlineData("http://flags.example.com")]
    [InlineData("file:///flags")]
    [InlineData("https://user:password@flags.example.com")]
    [InlineData("https://flags.example.com?environment=production")]
    [InlineData("https://flags.example.com#fragment")]
    public void Invalid_endpoint_is_rejected_before_any_request(string? endpoint)
    {
        using var http = new HttpClient();
        var options = new TinyFlagsHttpOptions
        {
            Endpoint = endpoint is null ? null : new Uri(endpoint, UriKind.RelativeOrAbsolute),
            ApiKey = "test-key"
        };

        var error = Assert.Throws<ArgumentException>(() => CreateClient(http, options));

        Assert.Equal(nameof(TinyFlagsHttpOptions.Endpoint), error.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("key with spaces")]
    [InlineData("key\r\nInjected: value")]
    [InlineData("key\u007f")]
    [InlineData("keyé")]
    public void Invalid_credential_is_rejected_without_echoing_it(string? key)
    {
        using var http = new HttpClient();
        var options = new TinyFlagsHttpOptions { Endpoint = new Uri("https://flags.example.com"), ApiKey = key };

        var error = Assert.Throws<ArgumentException>(() => CreateClient(http, options));

        Assert.Equal(nameof(TinyFlagsHttpOptions.ApiKey), error.ParamName);
        if (!string.IsNullOrWhiteSpace(key))
        {
            Assert.DoesNotContain(key, error.Message);
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(2147483648L)]
    public void Invalid_timeout_is_rejected_before_any_request(long milliseconds)
    {
        using var http = new HttpClient();
        var options = new TinyFlagsHttpOptions
        {
            Endpoint = new Uri("https://flags.example.com"), ApiKey = "test-key",
            RequestTimeout = TimeSpan.FromMilliseconds(milliseconds)
        };

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => CreateClient(http, options));

        Assert.Equal(nameof(TinyFlagsHttpOptions.RequestTimeout), error.ParamName);
    }
    private static TinyFlagsApiClient CreateClient(HttpClient http, TinyFlagsHttpOptions options)
        => new(http, options, new FeatureSnapshotReader(options.MaxSnapshotBytes));
}
