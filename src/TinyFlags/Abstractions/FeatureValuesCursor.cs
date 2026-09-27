using System;

namespace TinyFlags;

/// <summary>
/// Identifies the environment and revision of an accepted value snapshot.
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
