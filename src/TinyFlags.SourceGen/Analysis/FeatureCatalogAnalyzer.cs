using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Analysis;

internal static class FeatureCatalogAnalyzer
{
    public static FeatureIssue? Analyze(Compilation compilation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var conflict = FindNameConflict(compilation.Assembly.GlobalNamespace);
        if (conflict is null)
        {
            return null;
        }

        var location = conflict.Locations.First(candidate => candidate.IsInSource);
        var source = SourceLocationReader.Read(compilation, location);

        return new FeatureIssue("TFG005", conflict.Name, source);
    }

    private static ISymbol? FindNameConflict(INamespaceSymbol assemblyNamespace)
    {
        var currentNamespace = assemblyNamespace;
        foreach (var name in new[] { "TinyFlags", "Generated", "ThisAssemblyFeatureCatalog" })
        {
            var type = currentNamespace.GetTypeMembers(name, arity: 0).FirstOrDefault();
            if (type is not null)
            {
                return type;
            }

            var child = currentNamespace.GetNamespaceMembers().FirstOrDefault(member => member.Name == name);
            if (child is null)
            {
                return null;
            }

            if (name == "ThisAssemblyFeatureCatalog")
            {
                return child;
            }

            currentNamespace = child;
        }

        return null;
    }
}
