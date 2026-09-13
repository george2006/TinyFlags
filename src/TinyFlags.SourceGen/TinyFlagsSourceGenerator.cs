using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TinyFlags.SourceGen.Analysis;
using TinyFlags.SourceGen.Diagnostics;
using TinyFlags.SourceGen.Discovery;
using TinyFlags.SourceGen.Generation;
using TinyFlags.SourceGen.Validation;

namespace TinyFlags.SourceGen;

[Generator(LanguageNames.CSharp)]
public sealed class TinyFlagsSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var analysis = context.SyntaxProvider
            .CreateSyntaxProvider(
                FeatureProviderDiscovery.IsCandidateDeclaration,
                static (candidate, cancellationToken) =>
                    new FeatureProviderAnalyzer().Analyze(candidate, cancellationToken))
            .WithTrackingName("FeatureAnalysis")
            .Where(static provider => provider is not null)
            .Select(static (provider, _) => provider!);

        var validation = analysis.Select(static (provider, cancellationToken) =>
            new FeatureDeclarationValidator().Validate(provider, cancellationToken))
            .WithTrackingName("FeatureValidation");

        var sources = validation
            .SelectMany(static (result, _) => result.Providers)
            .Select(static (provider, cancellationToken) =>
                new FeatureGeneration().Generate(provider, cancellationToken))
            .WithTrackingName("FeatureGeneration");

        context.RegisterSourceOutput(sources, static (output, source) =>
            output.AddSource(source.HintName, SourceText.From(source.Source, Encoding.UTF8)));

        // Rebind cached issues to the current compilation's syntax trees before reporting.
        context.RegisterSourceOutput(validation.Combine(context.CompilationProvider), static (output, input) =>
        {
            foreach (var issue in input.Left.Diagnostics)
            {
                output.ReportDiagnostic(FeatureDiagnosticReporter.Create(input.Right, issue));
            }
        });
    }
}
