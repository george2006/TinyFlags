namespace TinyFlags.Tests;

public sealed class TinyFlagsClientOptionsTests
{
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
        var options = new TinyFlagsClientOptions
        {
            Endpoint = endpoint is null ? null : new Uri(endpoint, UriKind.RelativeOrAbsolute),
            ApiKey = "test-key"
        };

        var error = Assert.Throws<ArgumentException>(() => new TinyFlagsApiClient(http, options));

        Assert.Equal(nameof(TinyFlagsClientOptions.Endpoint), error.ParamName);
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
        var options = new TinyFlagsClientOptions { Endpoint = new Uri("https://flags.example.com"), ApiKey = key };

        var error = Assert.Throws<ArgumentException>(() => new TinyFlagsApiClient(http, options));

        Assert.Equal(nameof(TinyFlagsClientOptions.ApiKey), error.ParamName);
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
        var options = new TinyFlagsClientOptions
        {
            Endpoint = new Uri("https://flags.example.com"), ApiKey = "test-key",
            RequestTimeout = TimeSpan.FromMilliseconds(milliseconds)
        };

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new TinyFlagsApiClient(http, options));

        Assert.Equal(nameof(TinyFlagsClientOptions.RequestTimeout), error.ParamName);
    }
}
