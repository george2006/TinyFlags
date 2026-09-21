using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TinyFlags.SourceGen.Analysis;
using TinyFlags.SourceGen.Diagnostics;
using TinyFlags.SourceGen.Discovery;
using TinyFlags.SourceGen.Generation;
using TinyFlags.SourceGen.Generation.Planning;
using TinyFlags.SourceGen.Model;
using TinyFlags.SourceGen.Validation;

namespace TinyFlags.SourceGen;

[Generator(LanguageNames.CSharp)]
public sealed class TinyFlagsSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var analysis = Analyze(context.SyntaxProvider);
        var validation = Validate(analysis);
        var providers = ExtractValidProviders(validation);

        GenerateAccessClasses(context, providers);
        RegisterCatalog(context, providers);
        ReportValidationDiagnostics(context, validation);
    }

    private static IncrementalValuesProvider<FeatureProviderAnalysis> Analyze(SyntaxValueProvider syntaxProvider)
    {
        return syntaxProvider
            .CreateSyntaxProvider(
                FeatureProviderDiscovery.IsCandidateDeclaration,
                static (candidate, cancellationToken) =>
                    new FeatureProviderAnalyzer().Analyze(candidate, cancellationToken))
            .WithTrackingName("FeatureAnalysis")
            .Where(static provider => provider is not null)
            .Select(static (provider, _) => provider!);
    }

    private static IncrementalValuesProvider<FeatureValidationResult> Validate(
        IncrementalValuesProvider<FeatureProviderAnalysis> analysis)
    {
        return analysis
            .Select(static (provider, cancellationToken) =>
                new FeatureDeclarationValidator().Validate(provider, cancellationToken))
            .WithTrackingName("FeatureValidation");
    }

    private static IncrementalValuesProvider<FeatureProviderDefinition> ExtractValidProviders(
        IncrementalValuesProvider<FeatureValidationResult> validation)
    {
        return validation.SelectMany(static (result, _) => result.Providers);
    }

    private static void GenerateAccessClasses(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<FeatureProviderDefinition> providers)
    {
        var sources = providers
            .Select(static (provider, cancellationToken) =>
                new FeatureGeneration().Generate(provider, cancellationToken))
            .WithTrackingName("FeatureGeneration");

        context.RegisterSourceOutput(sources, static (output, source) =>
            output.AddSource(source.HintName, SourceText.From(source.Source, Encoding.UTF8)));
    }

    private static void ReportValidationDiagnostics(
        IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<FeatureValidationResult> validation)
    {
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
        var plans = PlanCatalog(providers);
        var issues = AnalyzeCatalogConflicts(context.CompilationProvider);
        var sources = GenerateCatalog(plans, issues);

        context.RegisterSourceOutput(sources, static (output, source) =>
        {
            if (source is { } catalog)
            {
                output.AddSource(catalog.HintName, SourceText.From(catalog.Source, Encoding.UTF8));
            }
        });
        ReportCatalogDiagnostics(context, issues);
    }

    private static IncrementalValueProvider<FeatureCatalogPlan> PlanCatalog(
        IncrementalValuesProvider<FeatureProviderDefinition> providers)
    {
        return providers.Collect()
            .Select(static (definitions, cancellationToken) =>
                new FeatureGeneration().PlanCatalog(definitions, cancellationToken))
            .WithTrackingName("FeatureCatalogPlanning");
    }

    private static IncrementalValueProvider<FeatureIssue?> AnalyzeCatalogConflicts(
        IncrementalValueProvider<Compilation> compilationProvider)
    {
        return compilationProvider.Select(static (compilation, cancellationToken) =>
            FeatureCatalogAnalyzer.Analyze(compilation, cancellationToken));
    }

    private static IncrementalValueProvider<(string HintName, string Source)?> GenerateCatalog(
        IncrementalValueProvider<FeatureCatalogPlan> plans,
        IncrementalValueProvider<FeatureIssue?> issues)
    {
        return plans.Combine(issues)
            .Select(static (input, cancellationToken) => CreateCatalogSource(input.Left, input.Right, cancellationToken))
            .WithTrackingName("FeatureCatalogGeneration");
    }

    private static (string HintName, string Source)? CreateCatalogSource(
        FeatureCatalogPlan plan, FeatureIssue? conflict, CancellationToken cancellationToken)
    {
        if (conflict is not null)
        {
            return null;
        }

        return new FeatureGeneration().GenerateCatalog(plan, cancellationToken);
    }

    private static void ReportCatalogDiagnostics(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<FeatureIssue?> issues)
    {
        context.RegisterSourceOutput(issues.Combine(context.CompilationProvider), static (output, input) =>
        {
            if (input.Left is not null)
            {
                output.ReportDiagnostic(FeatureDiagnosticReporter.Create(input.Right, input.Left));
            }
        });
    }
}
