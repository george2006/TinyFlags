using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TinyFlags;

/// <summary>
/// What a values transport hands back from one attempt: either a fresh, complete snapshot, or an
/// explicit "nothing changed" with no snapshot at all. Returned by
/// <see cref="IFeatureValuesTransport.GetValuesAsync"/> and yielded by
/// <see cref="IFeatureValuesSubscription.WatchAsync"/> - the same type serves both pull and push,
/// so a worker draining either one handles just one result shape.
/// </summary>
public sealed class FeatureValuesResult
{
    public FeatureValuesCursor? Cursor { get; }
    public IReadOnlyDictionary<string, object>? Values { get; }

    /// <summary>
    /// True when this result carries no new snapshot - <see cref="Cursor"/> and
    /// <see cref="Values"/> are both null in that case.
    /// </summary>
    public bool IsUnchanged => Cursor is null;

    /// <summary>
    /// A fresh, complete snapshot. <paramref name="values"/> is defensively copied into an
    /// immutable dictionary, so a caller mutating its own copy afterward can never affect this
    /// result.
    /// </summary>
    public static FeatureValuesResult Updated(FeatureValuesCursor cursor, IReadOnlyDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(values);
        return new FeatureValuesResult(cursor, new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(values)));
    }

    /// <summary>
    /// Nothing changed since the cursor the caller already had. A push transport should never
    /// yield this - there's nothing to report until something actually changes.
    /// </summary>
    public static FeatureValuesResult Unchanged() => new(null, null);

    private FeatureValuesResult(FeatureValuesCursor? cursor, IReadOnlyDictionary<string, object>? values)
    {
        Cursor = cursor;
        Values = values;
    }
}
