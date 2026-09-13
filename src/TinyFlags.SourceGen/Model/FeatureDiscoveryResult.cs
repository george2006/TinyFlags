using System.Collections.Immutable;
using System.Linq;

namespace TinyFlags.SourceGen.Model;

internal sealed class FeatureDiscoveryResult
{
    public FeatureDiscoveryResult(
        ImmutableArray<FeatureProviderDefinition> providers,
        ImmutableArray<FeatureIssue> diagnostics)
    {
        Providers = providers;
        Diagnostics = diagnostics;
    }

    public ImmutableArray<FeatureProviderDefinition> Providers { get; }

    public ImmutableArray<FeatureIssue> Diagnostics { get; }

    public override bool Equals(object? obj)
    {
        return obj is FeatureDiscoveryResult other
            && Providers.SequenceEqual(other.Providers)
            && Diagnostics.SequenceEqual(other.Diagnostics);
    }

    public override int GetHashCode()
    {
        var hash = 17;

        foreach (var item in Providers)
        {
            hash = unchecked(hash * 31 + item.GetHashCode());
        }

        foreach (var item in Diagnostics)
        {
            hash = unchecked(hash * 31 + item.GetHashCode());
        }

        return hash;
    }
}
