using System.Collections.Immutable;
using System.Linq;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Generation.Planning;

internal sealed class FeatureCatalogPlan
{
    public FeatureCatalogPlan(ImmutableArray<FeatureDefinition> features)
    {
        Features = features;
    }

    public ImmutableArray<FeatureDefinition> Features { get; }

    public override bool Equals(object? obj)
    {
        return obj is FeatureCatalogPlan other && Features.SequenceEqual(other.Features);
    }

    public override int GetHashCode()
    {
        var hash = 17;
        foreach (var feature in Features)
        {
            hash = unchecked(hash * 31 + feature.GetHashCode());
        }

        return hash;
    }
}
