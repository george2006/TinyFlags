using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TinyFlags.SourceGen.Discovery;

internal static class FeatureProviderDiscovery
{
    public static bool IsCandidateDeclaration(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is TypeDeclarationSyntax declaration && declaration.BaseList is not null;
    }
}
