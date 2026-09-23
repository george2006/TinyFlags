using System.Linq;
using Microsoft.CodeAnalysis;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Analysis;

/// <summary>
/// Converts a Roslyn <see cref="Location"/> into a plain tree-index-plus-span
/// <see cref="SourceLocation"/> - the point where location data crosses into the Roslyn-free
/// Model/ types. FeatureDiagnosticReporter converts it back to a real Location for reporting.
/// </summary>
internal static class SourceLocationReader
{
    public static SourceLocation Read(Compilation compilation, Location location)
    {
        var treeIndex = compilation.SyntaxTrees.TakeWhile(tree => tree != location.SourceTree).Count();
        return new SourceLocation(treeIndex, location.SourceSpan.Start, location.SourceSpan.Length);
    }
}
