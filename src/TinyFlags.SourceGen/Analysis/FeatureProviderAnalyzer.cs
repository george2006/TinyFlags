using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Analysis;

internal sealed class FeatureProviderAnalyzer
{
    public FeatureProviderAnalysis Analyze(
        Compilation compilation,
        INamedTypeSymbol provider,
        CancellationToken cancellationToken)
    {
        var properties = ImmutableArray.CreateBuilder<FeaturePropertyAnalysis>();

        foreach (var property in provider.GetMembers().OfType<IPropertySymbol>().OrderBy(
                     property => property.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            properties.Add(AnalyzeProperty(compilation, property, cancellationToken));
        }

        var namespaceName = provider.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : provider.ContainingNamespace.ToDisplayString();
        var qualifiedName = provider.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return new FeatureProviderAnalysis(
            provider.Name, namespaceName, qualifiedName, IsSupportedProvider(provider),
            ReadLocation(compilation, provider.Locations[0]), properties.ToImmutable());
    }

    private static FeaturePropertyAnalysis AnalyzeProperty(
        Compilation compilation,
        IPropertySymbol property,
        CancellationToken cancellationToken)
    {
        var syntax = property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        var expression = ReadDefaultExpression(syntax as PropertyDeclarationSyntax);

        var defaultValue = expression is null
            ? default
            : compilation.GetSemanticModel(expression.SyntaxTree).GetConstantValue(expression, cancellationToken);

        return new FeaturePropertyAnalysis(
            property.Name, ReadKind(property), HasSupportedPropertyShape(property),
            defaultValue.HasValue, defaultValue.HasValue ? defaultValue.Value : null,
            ReadLocation(compilation, property.Locations[0]),
            ReadLocation(compilation, expression?.GetLocation() ?? property.Locations[0]));
    }

    private static ExpressionSyntax? ReadDefaultExpression(PropertyDeclarationSyntax? property)
    {
        if (property is null)
        {
            return null;
        }

        if (property.ExpressionBody is not null)
        {
            return property.ExpressionBody.Expression;
        }

        var getter = property.AccessorList?.Accessors.FirstOrDefault(
            accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));

        if (getter?.ExpressionBody is not null)
        {
            return getter.ExpressionBody.Expression;
        }

        if (getter?.Body is not null)
        {
            return getter.Body.Statements.Count == 1
                ? (getter.Body.Statements[0] as ReturnStatementSyntax)?.Expression
                : null;
        }

        return getter is not null ? property.Initializer?.Value : null;
    }

    private static bool IsSupportedProvider(INamedTypeSymbol provider)
    {
        var isConcreteClass = provider.TypeKind == TypeKind.Class && !provider.IsAbstract;
        var isOrdinaryClass = !provider.IsRecord && !provider.IsFileLocal;
        var isTopLevelNonGeneric = provider.ContainingType is null && !provider.IsGenericType;
        var hasNoBaseClass = provider.BaseType?.SpecialType == SpecialType.System_Object;

        return isConcreteClass && isOrdinaryClass && isTopLevelNonGeneric && hasNoBaseClass;
    }

    private static bool HasSupportedPropertyShape(IPropertySymbol property)
    {
        var isPublicInstance = property.DeclaredAccessibility == Accessibility.Public && !property.IsStatic;
        var isReadOnly = property.GetMethod is not null && property.SetMethod is null;
        var isOrdinaryProperty = !property.IsIndexer && property.RefKind == RefKind.None;

        return isPublicInstance && isReadOnly && isOrdinaryProperty;
    }

    private static FeatureValueKind? ReadKind(IPropertySymbol property)
    {
        if (property.Type.SpecialType == SpecialType.System_Boolean)
        {
            return FeatureValueKind.Boolean;
        }

        return property.Type.SpecialType == SpecialType.System_String
            && property.NullableAnnotation != NullableAnnotation.Annotated
                ? FeatureValueKind.String
                : null;
    }

    private static SourceLocation ReadLocation(Compilation compilation, Location location)
    {
        var treeIndex = compilation.SyntaxTrees.TakeWhile(tree => tree != location.SourceTree).Count();
        return new SourceLocation(treeIndex, location.SourceSpan.Start, location.SourceSpan.Length);
    }
}
