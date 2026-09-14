using System.Collections.Immutable;
using System.Linq;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Generation.Planning;

internal sealed class FeatureCatalogPlan
{
    public FeatureCatalogPlan(ImmutableArray<FeatureDefinition> features, ImmutableArray<string> accessClassNames)
    {
        Features = features;
        AccessClassNames = accessClassNames;
    }

    public ImmutableArray<FeatureDefinition> Features { get; }

    public ImmutableArray<string> AccessClassNames { get; }

    public override bool Equals(object? obj)
    {
        return obj is FeatureCatalogPlan other
            && Features.SequenceEqual(other.Features)
            && AccessClassNames.SequenceEqual(other.AccessClassNames);
    }

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var feature in Features)
        {
            hash = unchecked(hash * 31 + feature.GetHashCode());
        }

        foreach (var className in AccessClassNames)
        {
            hash = unchecked(hash * 31 + className.GetHashCode());
        }

        return hash;
    }
}
