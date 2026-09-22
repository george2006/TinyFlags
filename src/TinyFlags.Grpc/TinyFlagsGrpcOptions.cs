using System;

namespace TinyFlags;

/// <summary>
/// Configures authenticated gRPC access to one TinyFlags server environment.
/// </summary>
public sealed class TinyFlagsGrpcOptions
{
    public Uri? Endpoint { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>How long to wait before reconnecting a dropped Watch stream.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    internal TinyFlagsGrpcOptions CreateSnapshot()
    {
        Validate();
        return new TinyFlagsGrpcOptions { Endpoint = Endpoint, ApiKey = ApiKey, ReconnectDelay = ReconnectDelay };
    }

    internal bool HasSameConfigurationAs(TinyFlagsGrpcOptions other)
        => Endpoint == other.Endpoint && ApiKey == other.ApiKey && ReconnectDelay == other.ReconnectDelay;

    internal void Validate()
    {
        // Same rule as TinyFlagsHttpOptions, same reason: the Bearer token travels as call
        // metadata, exactly as leakable over plain HTTP to a remote host as an Authorization header.
        if (Endpoint is null || !Endpoint.IsAbsoluteUri
            || (Endpoint.Scheme != Uri.UriSchemeHttps && !(Endpoint.Scheme == Uri.UriSchemeHttp && Endpoint.IsLoopback))
            || Endpoint.UserInfo.Length != 0 || Endpoint.Query.Length != 0 || Endpoint.Fragment.Length != 0)
        {
            throw new ArgumentException("Endpoint must be an absolute HTTPS base URI, or HTTP on loopback, without credentials, query or fragment.", nameof(Endpoint));
        }

        if (string.IsNullOrEmpty(ApiKey))
        {
            throw new ArgumentException("ApiKey is required.", nameof(ApiKey));
        }

        if (ReconnectDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ReconnectDelay), "ReconnectDelay must be positive.");
        }
    }
}
