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
        var tinyFlagsType = FindTypeMember(assemblyNamespace, "TinyFlags");
        if (tinyFlagsType is not null)
        {
            return tinyFlagsType;
        }

        var tinyFlagsNamespace = FindNamespaceMember(assemblyNamespace, "TinyFlags");
        if (tinyFlagsNamespace is null)
        {
            return null;
        }

        var generatedType = FindTypeMember(tinyFlagsNamespace, "Generated");
        if (generatedType is not null)
        {
            return generatedType;
        }

        var generatedNamespace = FindNamespaceMember(tinyFlagsNamespace, "Generated");
        if (generatedNamespace is null)
        {
            return null;
        }

        var catalogType = FindTypeMember(generatedNamespace, "ThisAssemblyFeatureCatalog");
        if (catalogType is not null)
        {
            return catalogType;
        }

        return FindNamespaceMember(generatedNamespace, "ThisAssemblyFeatureCatalog");
    }

    private static INamedTypeSymbol? FindTypeMember(INamespaceSymbol containingNamespace, string name)
        => containingNamespace.GetTypeMembers(name, arity: 0).FirstOrDefault();

    private static INamespaceSymbol? FindNamespaceMember(INamespaceSymbol containingNamespace, string name)
        => containingNamespace.GetNamespaceMembers().FirstOrDefault(member => member.Name == name);
}
