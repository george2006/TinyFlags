namespace TinyFlags;

/// <summary>
/// Transport-agnostic classification of an unrecoverable <see cref="TinyFlagsClientException"/> -
/// the same five buckets regardless of whether the transport speaks HTTP, gRPC, or anything else.
/// Workers only ever see this enum, never a wire-level status code.
/// </summary>
public enum TinyFlagsClientFailure
{
    /// <summary>The credential itself is invalid - missing, malformed, unknown, or revoked.</summary>
    CredentialsRejected,

    /// <summary>The credential is valid but lacks the permission this call needed.</summary>
    AccessDenied,

    /// <summary>Registration only: a key's kind or default disagrees with what's already registered.</summary>
    DefinitionsConflict,

    /// <summary>Any other request the server rejected as invalid.</summary>
    RequestRejected,

    /// <summary>Anything unclassified, or a response/message that doesn't parse.</summary>
    InvalidResponse
}
