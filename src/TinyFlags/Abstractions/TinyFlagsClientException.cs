using System;

namespace TinyFlags;

/// <summary>
/// A transport failure a worker cannot recover from on its own - permission, credentials, a
/// rejected or unparseable request. A transport should only ever throw this for genuinely
/// unrecoverable outcomes; anything it can retry internally (a transient network blip, a dropped
/// connection) should never surface as an exception at all. See
/// <see cref="TinyFlagsRegistrationWorker"/>, <see cref="TinyFlagsSynchronizationWorker"/> and
/// <see cref="TinyFlagsValuesWatchWorker"/> for how each worker reacts to this.
/// </summary>
public sealed class TinyFlagsClientException : Exception
{
    public TinyFlagsClientFailure Failure { get; }

    public TinyFlagsClientException(TinyFlagsClientFailure failure)
        : base($"TinyFlags operation failed: {failure}.") => Failure = failure;
}
