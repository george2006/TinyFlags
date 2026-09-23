using System.Collections.Immutable;
using System.Threading;
using TinyFlags.SourceGen.Generation.Emission;
using TinyFlags.SourceGen.Generation.Planning;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Generation;

/// <summary>
/// Thin facade over Planning and Emission for both generated outputs - one provider's access
/// class, and the per-assembly catalog - so TinyFlagsSourceGenerator only ever talks to this one
/// entry point rather than the planner/emitter pairs directly.
/// </summary>
internal sealed class FeatureGeneration
{
    public (string HintName, string Source) Generate(
        FeatureProviderDefinition provider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = new FeatureAccessPlanner().Create(provider);
        var source = new FeatureAccessEmitter().Emit(plan, cancellationToken);

        return (plan.HintName, source);
    }

    public FeatureCatalogPlan PlanCatalog(
        ImmutableArray<FeatureProviderDefinition> providers,
        CancellationToken cancellationToken)
    {
        return new FeatureCatalogPlanner().Create(providers, cancellationToken);
    }

    public (string HintName, string Source) GenerateCatalog(
        FeatureCatalogPlan plan,
        CancellationToken cancellationToken)
    {
        var source = new FeatureCatalogEmitter().Emit(plan, cancellationToken);
        return ("TinyFlags.Generated.ThisAssemblyFeatureCatalog.g.cs", source);
    }
}
