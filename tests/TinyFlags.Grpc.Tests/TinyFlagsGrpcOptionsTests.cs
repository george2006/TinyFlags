namespace TinyFlags.Grpc.Tests;

public sealed class TinyFlagsGrpcOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("relative/path")]
    [InlineData("file:///flags")]
    [InlineData("http://flags.example.com")]
    [InlineData("https://user:password@flags.example.com")]
    [InlineData("https://flags.example.com?environment=production")]
    [InlineData("https://flags.example.com#fragment")]
    public void Invalid_endpoint_is_rejected_before_any_call(string? endpoint)
    {
        var options = new TinyFlagsGrpcOptions
        {
            Endpoint = endpoint is null ? null : new Uri(endpoint, UriKind.RelativeOrAbsolute),
            ApiKey = "test-key"
        };

        var error = Assert.Throws<ArgumentException>(() => options.CreateSnapshot());

        Assert.Equal(nameof(TinyFlagsGrpcOptions.Endpoint), error.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Invalid_credential_is_rejected_before_any_call(string? key)
    {
        var options = new TinyFlagsGrpcOptions { Endpoint = new Uri("https://flags.example.com"), ApiKey = key };

        var error = Assert.Throws<ArgumentException>(() => options.CreateSnapshot());

        Assert.Equal(nameof(TinyFlagsGrpcOptions.ApiKey), error.ParamName);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Invalid_reconnect_delay_is_rejected_before_any_call(long milliseconds)
    {
        var options = new TinyFlagsGrpcOptions
        {
            Endpoint = new Uri("https://flags.example.com"), ApiKey = "test-key",
            ReconnectDelay = TimeSpan.FromMilliseconds(milliseconds)
        };

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => options.CreateSnapshot());

        Assert.Equal(nameof(TinyFlagsGrpcOptions.ReconnectDelay), error.ParamName);
    }

    [Fact]
    public void Configuration_equality_reflects_the_snapshot_at_the_time_it_was_taken()
    {
        var options = new TinyFlagsGrpcOptions { Endpoint = new Uri("https://flags.example.com"), ApiKey = "test-key" };
        var snapshot = options.CreateSnapshot();
        Assert.True(snapshot.HasSameConfigurationAs(options.CreateSnapshot()));

        options.ApiKey = "other-key";

        Assert.Equal("test-key", snapshot.ApiKey);
        Assert.False(snapshot.HasSameConfigurationAs(options.CreateSnapshot()));
    }
}
