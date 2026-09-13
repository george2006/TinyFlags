namespace TinyFlags.SourceGen.Model;

internal sealed class FeaturePropertyAnalysis
{
    public FeaturePropertyAnalysis(
        string name,
        FeatureValueKind? kind,
        bool hasSupportedShape,
        bool hasConstantDefault,
        object? defaultValue,
        SourceLocation location,
        SourceLocation defaultLocation)
    {
        Name = name;
        Kind = kind;
        HasSupportedShape = hasSupportedShape;
        HasConstantDefault = hasConstantDefault;
        DefaultValue = defaultValue;
        Location = location;
        DefaultLocation = defaultLocation;
    }

    public string Name { get; }

    public FeatureValueKind? Kind { get; }

    public bool HasSupportedShape { get; }

    public bool HasConstantDefault { get; }

    public object? DefaultValue { get; }

    public SourceLocation Location { get; }

    public SourceLocation DefaultLocation { get; }

    public override bool Equals(object? obj)
    {
        return obj is FeaturePropertyAnalysis other
            && Equals(Name, other.Name)
            && Equals(Kind, other.Kind)
            && Equals(HasSupportedShape, other.HasSupportedShape)
            && Equals(HasConstantDefault, other.HasConstantDefault)
            && Equals(DefaultValue, other.DefaultValue)
            && Equals(Location, other.Location)
            && Equals(DefaultLocation, other.DefaultLocation);
    }

    public override int GetHashCode()
    {
        return (Name, Kind, HasSupportedShape, HasConstantDefault, DefaultValue, Location, DefaultLocation).GetHashCode();
    }
}
