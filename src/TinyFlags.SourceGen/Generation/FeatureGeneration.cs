using System.Threading;
using TinyFlags.SourceGen.Generation.Emission;
using TinyFlags.SourceGen.Generation.Planning;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Generation;

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
}
