using System.Collections.Immutable;
using System.Threading;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Validation;

/// <summary>
/// Checks one analyzed provider and its properties against the five TFG0xx rules. A provider with
/// any issue contributes no <see cref="FeatureProviderDefinition"/> at all - partial success isn't
/// an option, since a definition built from an invalid provider would have nothing meaningful to
/// generate access code from.
/// </summary>
internal sealed class FeatureDeclarationValidator
{
    public FeatureValidationResult Validate(
        FeatureProviderAnalysis analysis,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var issues = ValidateProvider(analysis, cancellationToken);
        var providers = issues.IsEmpty
            ? ImmutableArray.Create(FeatureProviderDefinition.FromAnalysis(analysis))
            : ImmutableArray<FeatureProviderDefinition>.Empty;

        return new FeatureValidationResult(providers, issues);
    }

    private static ImmutableArray<FeatureIssue> ValidateProvider(
        FeatureProviderAnalysis analysis,
        CancellationToken cancellationToken)
    {
        if (!analysis.HasSupportedShape)
        {
            return ImmutableArray.Create(new FeatureIssue("TFG001", analysis.Name, analysis.Location));
        }

        var diagnostics = ImmutableArray.CreateBuilder<FeatureIssue>();

        if (analysis.HasGeneratedNameConflict)
        {
            diagnostics.Add(new FeatureIssue("TFG004", analysis.Name, analysis.Location));
        }

        foreach (var property in analysis.Properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateProperty(property, diagnostics);
        }

        return diagnostics.ToImmutable();
    }

    private static void ValidateProperty(
        FeaturePropertyAnalysis property,
        ImmutableArray<FeatureIssue>.Builder diagnostics)
    {
        if (!property.HasSupportedShape || property.Kind is null)
        {
            diagnostics.Add(new FeatureIssue("TFG002", property.Name, property.Location));
            return;
        }

        if (!HasSupportedDefault(property))
        {
            diagnostics.Add(new FeatureIssue("TFG003", property.Name, property.DefaultLocation));
        }
    }

    private static bool HasSupportedDefault(FeaturePropertyAnalysis property)
    {
        if (!property.HasConstantDefault)
        {
            return false;
        }

        return property.Kind == FeatureValueKind.Boolean
            ? property.DefaultValue is bool
            : property.DefaultValue is string;
    }
}
