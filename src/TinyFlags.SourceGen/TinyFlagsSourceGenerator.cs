using Microsoft.CodeAnalysis;
using TinyFlags.SourceGen.Analysis;
using TinyFlags.SourceGen.Diagnostics;
using TinyFlags.SourceGen.Validation;

namespace TinyFlags.SourceGen;

[Generator(LanguageNames.CSharp)]
public sealed class TinyFlagsSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var analysis = FeatureProviderDiscovery.CreateProvider(context);
        var discovery = analysis.Select(static (provider, cancellationToken) =>
            new FeatureDeclarationValidator().Validate(provider, cancellationToken))
            .WithTrackingName("FeatureValidation");

        // Rebind cached issues to the current compilation's syntax trees before reporting.
        context.RegisterSourceOutput(discovery.Combine(context.CompilationProvider), static (output, input) =>
        {
            foreach (var issue in input.Left.Diagnostics)
            {
                output.ReportDiagnostic(FeatureDiagnosticReporter.Create(input.Right, issue));
            }
        });
    }
}
