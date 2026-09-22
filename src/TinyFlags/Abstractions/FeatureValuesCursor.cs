using System;

namespace TinyFlags;

/// <summary>
/// Identifies a revision of an environment's values, independent of any transport. What a caller
/// already has, passed back in to ask "has this changed"; what a transport hands back on a change,
/// to pass in next time.
/// </summary>
public sealed class FeatureValuesCursor
{
    public Guid EnvironmentId { get; }
    public long Revision { get; }

    public FeatureValuesCursor(Guid environmentId, long revision)
    {
        EnvironmentId = environmentId;
        Revision = revision;
    }
}
