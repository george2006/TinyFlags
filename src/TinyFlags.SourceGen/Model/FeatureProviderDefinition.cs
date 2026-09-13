using System.Collections.Immutable;
using System.Linq;

namespace TinyFlags.SourceGen.Model;

internal sealed class FeatureProviderDefinition
{
    public FeatureProviderDefinition(
        string name,
        string namespaceName,
        string qualifiedName,
        ImmutableArray<FeatureDefinition> features)
    {
        Name = name;
        NamespaceName = namespaceName;
        QualifiedName = qualifiedName;
        Features = features;
    }

    public string Name { get; }

    public string NamespaceName { get; }

    public string QualifiedName { get; }

    public ImmutableArray<FeatureDefinition> Features { get; }

    // Only validated declarations can become definitions.
    public static FeatureProviderDefinition FromAnalysis(FeatureProviderAnalysis analysis)
    {
        var features = analysis.Properties.Select(property => new FeatureDefinition(
            property.Name,
            analysis.QualifiedName + "." + property.Name,
            property.Kind!.Value,
            property.DefaultValue!));

        return new FeatureProviderDefinition(
            analysis.Name, analysis.NamespaceName, analysis.QualifiedName, features.ToImmutableArray());
    }

    public override bool Equals(object? obj)
    {
        return obj is FeatureProviderDefinition other
            && Equals(Name, other.Name)
            && Equals(NamespaceName, other.NamespaceName)
            && Equals(QualifiedName, other.QualifiedName)
            && Features.SequenceEqual(other.Features);
    }

    public override int GetHashCode()
    {
        var hash = (Name, NamespaceName, QualifiedName).GetHashCode();

        foreach (var item in Features)
        {
            hash = unchecked(hash * 31 + item.GetHashCode());
        }

        return hash;
    }
}
