namespace TinyFlags.SourceGen.Model;

internal sealed class FeatureDefinition
{
    public FeatureDefinition(string name, string key, FeatureValueKind kind, object defaultValue)
    {
        Name = name;
        Key = key;
        Kind = kind;
        DefaultValue = defaultValue;
    }

    public string Name { get; }

    public string Key { get; }

    public FeatureValueKind Kind { get; }

    public object DefaultValue { get; }

    public override bool Equals(object? obj)
    {
        return obj is FeatureDefinition other
            && Equals(Name, other.Name)
            && Equals(Key, other.Key)
            && Equals(Kind, other.Kind)
            && Equals(DefaultValue, other.DefaultValue);
    }

    public override int GetHashCode()
    {
        return (Name, Key, Kind, DefaultValue).GetHashCode();
    }
}
