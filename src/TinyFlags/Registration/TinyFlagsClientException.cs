using System;

namespace TinyFlags;

internal sealed class TinyFlagsClientException : Exception
{
    public TinyFlagsClientFailure Failure { get; }

    public TinyFlagsClientException(TinyFlagsClientFailure failure)
        : base($"TinyFlags operation failed: {failure}.") => Failure = failure;
}
