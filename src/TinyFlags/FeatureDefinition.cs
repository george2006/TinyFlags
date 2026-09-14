using System;

namespace TinyFlags;

/// <summary>
/// Describes a flag's identity, supported type, and declared default for registration.
/// </summary>
public sealed class FeatureDefinition
{
    public string Key { get; }

    public FeatureKind Kind { get; }

    /// <summary>
    /// Gets the declared default as a boxed boolean or a non-null string, according to Kind.
    /// </summary>
    public object DefaultValue { get; }

    public static FeatureDefinition Boolean(string key, bool defaultValue)
    {
        return new FeatureDefinition(key, FeatureKind.Boolean, defaultValue);
    }

    public static FeatureDefinition String(string key, string defaultValue)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        return new FeatureDefinition(key, FeatureKind.String, defaultValue);
    }

    private FeatureDefinition(string key, FeatureKind kind, object defaultValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Kind = kind;
        DefaultValue = defaultValue;
    }
}
