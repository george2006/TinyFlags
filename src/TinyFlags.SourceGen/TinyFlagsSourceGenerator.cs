using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TinyFlags.SourceGen.Analysis;
using TinyFlags.SourceGen.Diagnostics;
using TinyFlags.SourceGen.Discovery;
using TinyFlags.SourceGen.Generation;
using TinyFlags.SourceGen.Model;
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

        var providers = validation.SelectMany(static (result, _) => result.Providers);
        var sources = providers
            .Select(static (provider, cancellationToken) =>
                new FeatureGeneration().Generate(provider, cancellationToken))
            .WithTrackingName("FeatureGeneration");

        context.RegisterSourceOutput(sources, static (output, source) =>
            output.AddSource(source.HintName, SourceText.From(source.Source, Encoding.UTF8)));

        RegisterCatalog(context, providers);

        // Rebind cached issues to the current compilation's syntax trees before reporting.
        context.RegisterSourceOutput(validation.Combine(context.CompilationProvider), static (output, input) =>
        {
            foreach (var issue in input.Left.Diagnostics)
            {
                output.ReportDiagnostic(FeatureDiagnosticReporter.Create(input.Right, issue));
            }
        });
    }

    private static void RegisterCatalog(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<FeatureProviderDefinition> providers)
    {
        var plans = providers.Collect()
            .Select(static (definitions, cancellationToken) =>
                new FeatureGeneration().PlanCatalog(definitions, cancellationToken))
            .WithTrackingName("FeatureCatalogPlanning");
        var issues = context.CompilationProvider
            .Select(static (compilation, cancellationToken) =>
                FeatureCatalogAnalyzer.Analyze(compilation, cancellationToken));
        var sources = plans.Combine(issues)
            .Select(static (input, cancellationToken) =>
                input.Right is null
                    ? new FeatureGeneration().GenerateCatalog(input.Left, cancellationToken)
                    : ((string HintName, string Source)?)null)
            .WithTrackingName("FeatureCatalogGeneration");

        context.RegisterSourceOutput(sources, static (output, source) =>
        {
            if (source is { } catalog)
            {
                output.AddSource(catalog.HintName, SourceText.From(catalog.Source, Encoding.UTF8));
            }
        });
        context.RegisterSourceOutput(issues.Combine(context.CompilationProvider), static (output, input) =>
        {
            if (input.Left is not null)
            {
                output.ReportDiagnostic(FeatureDiagnosticReporter.Create(input.Right, input.Left));
            }
        });
    }
}
