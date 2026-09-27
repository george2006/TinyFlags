using System;

namespace TinyFlags;

/// <summary>
/// A transport failure that was not recovered internally. Transient failures should be retried
/// by the transport. Invalid responses are retried on the next pull refresh; other failures
/// stop synchronization. Registration and push subscriptions stop on any such failure.
/// </summary>
public sealed class TinyFlagsClientException : Exception
{
    public TinyFlagsClientFailure Failure { get; }

    public TinyFlagsClientException(TinyFlagsClientFailure failure)
        : base($"TinyFlags operation failed: {failure}.") => Failure = failure;
}
