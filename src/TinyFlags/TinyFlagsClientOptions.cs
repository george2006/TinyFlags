using System;
using System.Linq;

namespace TinyFlags;

/// <summary>
/// Configures authenticated access to one TinyFlags server environment.
/// </summary>
public sealed class TinyFlagsClientOptions
{
    public Uri? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    internal TinyFlagsClientOptions CreateSnapshot()
    {
        Validate();
        return new TinyFlagsClientOptions
        {
            Endpoint = new Uri(Endpoint!.AbsoluteUri.TrimEnd('/') + "/"),
            ApiKey = ApiKey,
            RequestTimeout = RequestTimeout
        };
    }

    internal bool HasSameConfigurationAs(TinyFlagsClientOptions other)
        => Endpoint == other.Endpoint && ApiKey == other.ApiKey && RequestTimeout == other.RequestTimeout;

    internal void Validate()
    {
        if (Endpoint is null || !Endpoint.IsAbsoluteUri
            || (Endpoint.Scheme != Uri.UriSchemeHttps && !(Endpoint.Scheme == Uri.UriSchemeHttp && Endpoint.IsLoopback))
            || Endpoint.UserInfo.Length != 0 || Endpoint.Query.Length != 0 || Endpoint.Fragment.Length != 0)
        {
            throw new ArgumentException("Endpoint must be an absolute HTTPS base URI, or HTTP on loopback, without credentials, query or fragment.", nameof(Endpoint));
        }

        if (string.IsNullOrEmpty(ApiKey) || ApiKey.Any(character => character <= ' ' || character >= '\u007f'))
        {
            throw new ArgumentException("ApiKey must contain visible ASCII characters without whitespace.", nameof(ApiKey));
        }

        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMilliseconds(int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(nameof(RequestTimeout), "RequestTimeout must be positive and no greater than Int32.MaxValue milliseconds.");
        }
    }
}
