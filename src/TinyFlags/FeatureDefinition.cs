using System;

namespace TinyFlags;

/// <summary>
/// Describes a flag's identity, supported type, and declared default for registration.
/// </summary>
public sealed class FeatureDefinition
{
    /// <summary>
    /// The flag's identity: the fully qualified provider type name plus the property name.
    /// </summary>
    public string Key { get; }

    public FeatureKind Kind { get; }

    /// <summary>
    /// Gets the declared default as a boxed boolean or a non-null string, according to Kind.
    /// </summary>
    public object DefaultValue { get; }

    /// <summary>
    /// Creates a <see cref="FeatureKind.Boolean"/> definition.
    /// </summary>
    public static FeatureDefinition Boolean(string key, bool defaultValue)
    {
        return new FeatureDefinition(key, FeatureKind.Boolean, defaultValue);
    }

    /// <summary>
    /// Creates a <see cref="FeatureKind.String"/> definition. <paramref name="defaultValue"/> must
    /// be non-null; empty is fine.
    /// </summary>
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
