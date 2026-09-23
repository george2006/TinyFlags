using System;
using System.Linq;

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

        // Same rule as TinyFlagsHttpOptions: the Bearer token travels as call metadata, exactly as
        // leakable to logs or a misbehaving intermediary if it carries whitespace or control
        // characters as HTTP header text would be.
        if (string.IsNullOrEmpty(ApiKey) || ApiKey.Any(character => character <= ' ' || character >= '\u007f'))
        {
            throw new ArgumentException("ApiKey must contain visible ASCII characters without whitespace.", nameof(ApiKey));
        }

        if (ReconnectDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ReconnectDelay), "ReconnectDelay must be positive.");
        }
    }
}
