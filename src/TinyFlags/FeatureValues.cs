using System;
using System.Collections.Generic;
using System.Threading;

namespace TinyFlags;

public sealed class FeatureValues
{
    private Dictionary<string, object> snapshot = new(StringComparer.Ordinal);

    public bool GetBoolean(string key, bool defaultValue)
    {
        return ReadValue(key, defaultValue);
    }

    public string GetString(string key, string defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        return ReadValue(key, defaultValue);
    }

    public void ReplaceSnapshot(IReadOnlyDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var nextSnapshot = CopyValues(values);

        Interlocked.Exchange(ref snapshot, nextSnapshot);
    }

    private T ReadValue<T>(string key, T defaultValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var currentSnapshot = Volatile.Read(ref snapshot);

        return currentSnapshot.TryGetValue(key, out var value) && value is T typedValue
            ? typedValue
            : defaultValue;
    }

    private static Dictionary<string, object> CopyValues(IReadOnlyDictionary<string, object> values)
    {
        var copy = new Dictionary<string, object>(values.Count, StringComparer.Ordinal);

        foreach (var entry in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);

            if (entry.Value is not bool && entry.Value is not string)
            {
                throw new ArgumentException("Feature values must be booleans or non-null strings.", nameof(values));
            }

            copy.Add(entry.Key, entry.Value);
        }

        // Published dictionaries are private and never mutated, so readers need no lock.
        return copy;
    }
}
