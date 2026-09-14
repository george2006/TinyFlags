using Microsoft.CodeAnalysis;

namespace TinyFlags.SourceGen.Tests;

public sealed class MultiAssemblyCatalogTests
{
    [Fact]
    public void One_root_registration_resolves_host_and_transitive_library_flags_with_shared_updates()
    {
        var billing = CompileLibrary("Billing", """
            namespace Billing;
            public class Flags : TinyFlags.IFeatureProvider { public bool Enabled => false; }
            public static class Api { public static void Initialize() { } }
            """);
        var search = CompileLibrary("Search", """
            namespace Search;
            public class Flags : TinyFlags.IFeatureProvider { public string Text => "Find"; }
            public static class Api { public static void Initialize() => Billing.Api.Initialize(); }
            """, billing);
        const string host = """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TinyFlags;
            public class Root : IFeatureProvider { public bool Enabled => false; }
            public static class Scenario
            {
                public static bool Run()
                {
                    Search.Api.Initialize();
                    var services = new ServiceCollection().AddTinyFlags();
                    services.AddTinyFlags();
                    using var provider = services.BuildServiceProvider(new ServiceProviderOptions
                    {
                        ValidateOnBuild = true, ValidateScopes = true
                    });
                    using var scope = provider.CreateScope();
                    var billing = provider.GetRequiredService<Billing.FlagsFeatureFlags>();
                    var search = provider.GetRequiredService<Search.FlagsFeatureFlags>();
                    var root = provider.GetRequiredService<RootFeatureFlags>();
                    var defaults = !billing.Enabled && search.Text == "Find" && !root.Enabled;
                    provider.GetRequiredService<FeatureValues>().ReplaceSnapshot(new Dictionary<string, object>
                    {
                        ["Billing.Flags.Enabled"] = true,
                        ["Search.Flags.Text"] = "Updated",
                        ["Root.Enabled"] = true
                    });
                    return defaults && billing.Enabled && search.Text == "Updated" && root.Enabled
                        && ReferenceEquals(billing, scope.ServiceProvider.GetRequiredService<Billing.FlagsFeatureFlags>())
                        && ReferenceEquals(search, scope.ServiceProvider.GetRequiredService<Search.FlagsFeatureFlags>())
                        && ReferenceEquals(root, scope.ServiceProvider.GetRequiredService<RootFeatureFlags>())
                        && provider.GetServices<FeatureValues>().Count() == 1
                        && provider.GetServices<Billing.FlagsFeatureFlags>().Count() == 1
                        && provider.GetServices<Search.FlagsFeatureFlags>().Count() == 1
                        && provider.GetServices<RootFeatureFlags>().Count() == 1;
                }
            }
            """;

        Assert.True(ExecuteHost<bool>(host, billing, search));
        Assert.True(ExecuteHost<bool>(host, search, billing));
    }

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
