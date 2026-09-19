using System;

namespace TinyFlags;

internal sealed class FeatureValuesResult
{
    public FeatureSnapshot? Snapshot { get; }
    public bool IsUnchanged => Snapshot is null;

    public static FeatureValuesResult Updated(FeatureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new FeatureValuesResult(snapshot);
    }

    public static FeatureValuesResult Unchanged() => new(null);

    private FeatureValuesResult(FeatureSnapshot? snapshot) => Snapshot = snapshot;
}
