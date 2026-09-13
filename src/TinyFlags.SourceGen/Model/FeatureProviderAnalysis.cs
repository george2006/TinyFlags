using System.Collections.Immutable;
using System.Linq;

namespace TinyFlags.SourceGen.Model;

internal sealed class FeatureProviderAnalysis
{
    public FeatureProviderAnalysis(
        string name,
        string namespaceName,
        string qualifiedName,
        bool hasSupportedShape,
        bool hasGeneratedNameConflict,
        SourceLocation location,
        ImmutableArray<FeaturePropertyAnalysis> properties)
    {
        Name = name;
        NamespaceName = namespaceName;
        QualifiedName = qualifiedName;
        HasSupportedShape = hasSupportedShape;
        HasGeneratedNameConflict = hasGeneratedNameConflict;
        Location = location;
        Properties = properties;
    }

    public string Name { get; }

    public string NamespaceName { get; }

    public string QualifiedName { get; }

    public bool HasSupportedShape { get; }

    public bool HasGeneratedNameConflict { get; }

    public SourceLocation Location { get; }

    public ImmutableArray<FeaturePropertyAnalysis> Properties { get; }

    public override bool Equals(object? obj)
    {
        return obj is FeatureProviderAnalysis other
            && Equals(Name, other.Name)
            && Equals(NamespaceName, other.NamespaceName)
            && Equals(QualifiedName, other.QualifiedName)
            && Equals(HasSupportedShape, other.HasSupportedShape)
            && Equals(HasGeneratedNameConflict, other.HasGeneratedNameConflict)
            && Equals(Location, other.Location)
            && Properties.SequenceEqual(other.Properties);
    }

    public override int GetHashCode()
    {
        var hash = (Name, NamespaceName, QualifiedName, HasSupportedShape, HasGeneratedNameConflict, Location).GetHashCode();

        foreach (var item in Properties)
        {
            hash = unchecked(hash * 31 + item.GetHashCode());
        }

        return hash;
    }
}
