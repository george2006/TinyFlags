using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Generation.Planning;

internal sealed class FeatureCatalogPlanner
{
    public FeatureCatalogPlan Create(
        ImmutableArray<FeatureProviderDefinition> providers,
        CancellationToken cancellationToken)
    {
        var features = ImmutableArray.CreateBuilder<FeatureDefinition>();
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            features.AddRange(provider.Features);
        }

        return new FeatureCatalogPlan(features.OrderBy(feature => feature.Key, StringComparer.Ordinal).ToImmutableArray());
    }
}
