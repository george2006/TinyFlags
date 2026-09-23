using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TinyFlags.SourceGen.Discovery;

internal static class FeatureProviderDiscovery
{
    /// <summary>
    /// Cheap syntactic pre-filter, run before any semantic analysis: only a type declaration with
    /// a base list can possibly implement <c>IFeatureProvider</c>, so anything without one is
    /// excluded here rather than paying for a semantic model lookup that would reject it anyway.
    /// </summary>
    public static bool IsCandidateDeclaration(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is TypeDeclarationSyntax declaration && declaration.BaseList is not null;
    }
}
