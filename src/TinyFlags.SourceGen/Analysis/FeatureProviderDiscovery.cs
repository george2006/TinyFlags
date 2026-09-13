using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Analysis;

internal static class FeatureProviderDiscovery
{
    public static IncrementalValuesProvider<FeatureProviderAnalysis> CreateProvider(
        IncrementalGeneratorInitializationContext context)
    {
        return context.SyntaxProvider
            .CreateSyntaxProvider(IsCandidateDeclaration, AnalyzeProvider)
            .WithTrackingName("FeatureAnalysis")
            .Where(static provider => provider is not null)
            .Select(static (provider, _) => provider!);
    }

    private static bool IsCandidateDeclaration(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is TypeDeclarationSyntax declaration && declaration.BaseList is not null;
    }

    private static FeatureProviderAnalysis? AnalyzeProvider(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;
        var provider = context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken) as INamedTypeSymbol;
        var compilation = context.SemanticModel.Compilation;
        var marker = compilation.GetTypeByMetadataName("TinyFlags.IFeatureProvider");

        if (provider is null || provider.TypeKind == TypeKind.Interface || marker is null)
        {
            return null;
        }

        if (!provider.AllInterfaces.Any(contract => SymbolEqualityComparer.Default.Equals(contract, marker))
            || !IsFirstCandidate(provider, declaration, cancellationToken))
        {
            return null;
        }

        return new FeatureProviderAnalyzer().Analyze(compilation, provider, cancellationToken);
    }

    private static bool IsFirstCandidate(
        INamedTypeSymbol provider,
        TypeDeclarationSyntax declaration,
        CancellationToken cancellationToken)
    {
        // A partial provider has one pipeline entry, but analysis reads all its members.
        foreach (var reference in provider.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax candidate
                && candidate.BaseList is not null)
            {
                return candidate.SyntaxTree == declaration.SyntaxTree && candidate.Span == declaration.Span;
            }
        }

        return false;
    }
}
