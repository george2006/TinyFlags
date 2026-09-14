using Microsoft.CodeAnalysis;

namespace TinyFlags.SourceGen.Tests;

public sealed class MultiAssemblyCatalogTests
{
    [Fact]
    public void Root_composes_its_own_flags_and_initialized_library_contributions()
    {
        var billing = CompileLibrary("Billing", """
            namespace Billing;
            public class Flags : TinyFlags.IFeatureProvider { public bool Enabled => true; }
            public static class Api
            {
                public static bool Read() => new FlagsFeatureFlags(new TinyFlags.FeatureValues()).Enabled;
                public static int LocalCount() => TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions.Count;
            }
            """);
        var search = CompileLibrary("Search", """
            namespace Search;
            public class Flags : TinyFlags.IFeatureProvider { public string Text => "Find"; }
            public static class Api
            {
                public static string Read() => new FlagsFeatureFlags(new TinyFlags.FeatureValues()).Text;
                public static int LocalCount() => TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions.Count;
            }
            """);
        const string host = """
            using System.Linq;
            namespace Host
            {
                public class Flags : TinyFlags.IFeatureProvider { public string Label => "Home"; }
            }
            public static class Scenario
            {
                public static string[] Run()
                {
                    _ = Billing.Api.Read();
                    _ = Search.Api.Read();
                    var localCount = TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions.Count;
                    return TinyFlags.TinyFlagsBootstrap.GetDefinitions()
                        .Select(definition => definition.Key + "=" + definition.DefaultValue)
                        .Append("LocalCounts=" + Billing.Api.LocalCount() + "," + Search.Api.LocalCount() + "," + localCount)
                        .ToArray();
                }
            }
            """;

        var expected = new[] { "Billing.Flags.Enabled=True", "Host.Flags.Label=Home", "Search.Flags.Text=Find", "LocalCounts=1,1,1" };
        Assert.Equal(expected, ExecuteHost<string[]>(host, billing, search));
        Assert.Equal(expected, ExecuteHost<string[]>(host, search, billing));
    }

    [Fact]
    public void Referenced_library_contributions_are_initialized_through_a_transitive_call()
    {
        var leaf = CompileLibrary("Leaf", """
            namespace Leaf;
            public class Flags : TinyFlags.IFeatureProvider { public bool Enabled => true; }
            public static class Api { public static bool Read() => new FlagsFeatureFlags(new TinyFlags.FeatureValues()).Enabled; }
            """);
        var middle = CompileLibrary("Middle", """
            namespace Middle;
            public class Flags : TinyFlags.IFeatureProvider { public bool Enabled => false; }
            public static class Api { public static bool Read() => Leaf.Api.Read(); }
            """, leaf);
        const string host = """
            using System.Linq;
            public static class Scenario
            {
                public static string[] Run()
                {
                    _ = Middle.Api.Read();
                    return TinyFlags.TinyFlagsBootstrap.GetDefinitions().Select(definition => definition.Key).ToArray();
                }
            }
            """;

        Assert.Equal(new[] { "Leaf.Flags.Enabled", "Middle.Flags.Enabled" }, ExecuteHost<string[]>(host, leaf, middle));
    }

    [Fact]
    public void Repeated_registration_of_the_same_catalog_keeps_its_first_contribution()
    {
        var library = CompileLibrary("Library", """
            public class Flags : TinyFlags.IFeatureProvider { public bool Enabled => true; }
            public static class Api
            {
                public static void RegisterAgain()
                {
                    TinyFlags.Generated.ThisAssemblyFeatureCatalog.Initialize();
                    TinyFlags.TinyFlagsBootstrap.AddContribution(
                        typeof(TinyFlags.Generated.ThisAssemblyFeatureCatalog),
                        new[] { TinyFlags.FeatureDefinition.Boolean("Unexpected", false) });
                }
            }
            """);
        const string host = """
            using System.Linq;
            public static class Scenario
            {
                public static string[] Run()
                {
                    Api.RegisterAgain();
                    Api.RegisterAgain();
                    return TinyFlags.TinyFlagsBootstrap.GetDefinitions().Select(definition => definition.Key).ToArray();
                }
            }
            """;

        Assert.Equal(new[] { "Flags.Enabled" }, ExecuteHost<string[]>(host, library));
    }

    [Fact]
    public void Equivalent_keys_from_different_assemblies_are_composed_once()
    {
        var first = CompileLibrary("First", SharedFlag("First", "public bool Enabled => false;"));
        var second = CompileLibrary("Second", SharedFlag("Second", "public bool Enabled => false;"));
        const string host = """
            public static class Scenario
            {
                public static object[] Run()
                {
                    First.Api.Touch();
                    Second.Api.Touch();
                    var definitions = TinyFlags.TinyFlagsBootstrap.GetDefinitions();
                    return new object[] { definitions.Count, definitions[0].Key, definitions[0].Kind.ToString(), definitions[0].DefaultValue };
                }
            }
            """;

        Assert.Equal(new object[] { 1, "Shared.Flags.Enabled", "Boolean", false },
            ExecuteHost<object[]>(host, first, second));
    }

    [Theory]
    [InlineData("public bool Enabled => true;")]
    [InlineData("public string Enabled => \"false\";")]
    public void Conflicting_keys_from_different_assemblies_fail_when_the_root_composes(string declaration)
    {
        var first = CompileLibrary("First", SharedFlag("First", "public bool Enabled => false;"));
        var second = CompileLibrary("Second", SharedFlag("Second", declaration));
        const string host = """
            public static class Scenario
            {
                public static string Run()
                {
                    First.Api.Touch();
                    Second.Api.Touch();
                    try
                    {
                        TinyFlags.TinyFlagsBootstrap.GetDefinitions();
                        return "No conflict";
                    }
                    catch (System.InvalidOperationException error)
                    {
                        return error.Message;
                    }
                }
            }
            """;

        Assert.Contains("Shared.Flags.Enabled", ExecuteHost<string>(host, first, second));
    }

    private static string SharedFlag(string libraryName, string declaration)
    {
        return "namespace Shared { public class Flags : TinyFlags.IFeatureProvider { " + declaration + " } }"
            + "namespace " + libraryName + " { public static class Api { public static void Touch() { } } }";
    }

    private static byte[] CompileLibrary(string name, string source, params byte[][] references)
    {
        var compilation = SourceGeneratorTestHost.CreateCompilation(source).WithAssemblyName(name)
            .AddReferences(references.Select(reference => MetadataReference.CreateFromImage(reference)));
        return SourceGeneratorTestHost.CompileAssembly(compilation);
    }

    private static T ExecuteHost<T>(string source, params byte[][] libraries)
    {
        var host = CompileLibrary("Host", source, libraries);
        return SourceGeneratorTestHost.ExecuteAssembly<T>(host, libraries);
    }
}
