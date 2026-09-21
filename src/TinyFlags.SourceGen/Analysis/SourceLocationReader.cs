using System.Linq;
using Microsoft.CodeAnalysis;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Analysis;

internal static class SourceLocationReader
{
    public static SourceLocation Read(Compilation compilation, Location location)
    {
        var treeIndex = compilation.SyntaxTrees.TakeWhile(tree => tree != location.SourceTree).Count();
        return new SourceLocation(treeIndex, location.SourceSpan.Start, location.SourceSpan.Length);
    }
}
