using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TinyFlags;

internal sealed class FeatureSnapshot
{
    public Guid EnvironmentId { get; }
    public long Revision { get; }
    public IReadOnlyDictionary<string, object> Values { get; }

    internal FeatureSnapshot(Guid environmentId, long revision, Dictionary<string, object> values)
    {
        EnvironmentId = environmentId;
        Revision = revision;
        Values = new ReadOnlyDictionary<string, object>(values);
    }
}