using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Analysis;

/// <summary>
/// Checks whether user code already occupies the generated catalog's reserved name
/// (<c>TinyFlags.Generated.ThisAssemblyFeatureCatalog</c>) - reports TFG005 if so, since the
/// generator would otherwise silently fail to add its own declaration alongside a conflicting one.
/// </summary>
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

    // Walked segment by segment rather than one GetTypeByMetadataName("TinyFlags.Generated.
    // ThisAssemblyFeatureCatalog") call: a single lookup would miss a conflict where an earlier
    // segment (e.g. "TinyFlags" itself) is already a type instead of a namespace, which blocks the
    // generated declaration just as much as a conflict at the final segment does.
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
