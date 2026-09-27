using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TinyFlags;

/// <summary>
/// A complete value snapshot, or an unchanged result for a pull request.
/// </summary>
public sealed class FeatureValuesResult
{
    public FeatureValuesCursor? Cursor { get; }
    public IReadOnlyDictionary<string, object>? Values { get; }

    /// <summary>
    /// True when <see cref="Cursor"/> and <see cref="Values"/> are both null.
    /// </summary>
    public bool IsUnchanged => Cursor is null;

    /// <summary>
    /// Creates a complete snapshot with a read-only copy of <paramref name="values"/>.
    /// </summary>
    public static FeatureValuesResult Updated(FeatureValuesCursor cursor, IReadOnlyDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(values);
        return new FeatureValuesResult(cursor, new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(values)));
    }

    /// <summary>
    /// Indicates no change from the accepted cursor. Push transports must not yield this result.
    /// </summary>
    public static FeatureValuesResult Unchanged() => new(null, null);

    private FeatureValuesResult(FeatureValuesCursor? cursor, IReadOnlyDictionary<string, object>? values)
    {
        Cursor = cursor;
        Values = values;
    }
}
