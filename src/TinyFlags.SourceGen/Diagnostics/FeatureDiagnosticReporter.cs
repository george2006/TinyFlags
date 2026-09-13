using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Diagnostics;

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
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown feature diagnostic.")
        };
    }
}
