using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TinyFlags;

public sealed class FeatureValuesResult
{
    public FeatureValuesCursor? Cursor { get; }
    public IReadOnlyDictionary<string, object>? Values { get; }
    public bool IsUnchanged => Cursor is null;

    public static FeatureValuesResult Updated(FeatureValuesCursor cursor, IReadOnlyDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(values);
        return new FeatureValuesResult(cursor, new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(values)));
    }

    public static FeatureValuesResult Unchanged() => new(null, null);

    private FeatureValuesResult(FeatureValuesCursor? cursor, IReadOnlyDictionary<string, object>? values)
    {
        Cursor = cursor;
        Values = values;
    }
}
