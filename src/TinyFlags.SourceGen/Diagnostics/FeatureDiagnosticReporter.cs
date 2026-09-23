using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Diagnostics;

/// <summary>
/// Rebinds a Roslyn-free <see cref="FeatureIssue"/> to the current compilation's syntax trees and
/// produces a real <see cref="Diagnostic"/> - the inverse of SourceLocationReader, done at the
/// output boundary so cached issues from an earlier compilation still report against current trees.
/// </summary>
internal static class FeatureDiagnosticReporter
{
    public static Diagnostic Create(Compilation compilation, FeatureIssue issue)
    {
        var descriptor = GetDescriptor(issue.Id);
        var tree = compilation.SyntaxTrees.ElementAt(issue.Location.TreeIndex);
        var span = new TextSpan(issue.Location.Start, issue.Location.Length);

        return Diagnostic.Create(descriptor, Location.Create(tree, span), issue.MemberName);
    }

    private static DiagnosticDescriptor GetDescriptor(string id)
    {
        return id switch
        {
            "TFG001" => FeatureDiagnostics.UnsupportedProvider,
            "TFG002" => FeatureDiagnostics.UnsupportedProperty,
            "TFG003" => FeatureDiagnostics.NonConstantDefault,
            "TFG004" => FeatureDiagnostics.GeneratedNameConflict,
            "TFG005" => FeatureDiagnostics.CatalogNameConflict,
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown feature diagnostic.")
        };
    }
}
