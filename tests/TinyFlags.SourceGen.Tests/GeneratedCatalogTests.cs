using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TinyFlags.SourceGen.Tests;

public sealed class GeneratedCatalogTests
{
    [Fact]
    public void Catalog_preserves_typed_defaults_without_executing_declarations()
    {
        const string declaration = """
            namespace Shop;
            public class Checkout : TinyFlags.IFeatureProvider
            {
                public Checkout() => throw new System.InvalidOperationException();
                public bool Enabled => false;
                public string Label => "Comprar";
            }
            """;
        const string scenario = """
            public static class Scenario
            {
                public static object[] Run()
                {
                    var definitions = TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions;
                    return new object[]
                    {
                        definitions.Count,
                        definitions[0].Key, definitions[0].Kind.ToString(), definitions[0].DefaultValue,
                        definitions[1].Key, definitions[1].Kind.ToString(), definitions[1].DefaultValue
                    };
                }
            }
            """;

        Assert.Equal(new object[] { 2, "Shop.Checkout.Enabled", "Boolean", false, "Shop.Checkout.Label", "String", "Comprar" },
            SourceGeneratorTestHost.Execute<object[]>(declaration, scenario));
    }

    [Theory]
    [InlineData("")]
    [InlineData("public class Empty : TinyFlags.IFeatureProvider { }")]
    public void Empty_assemblies_have_an_empty_catalog(string declaration)
    {
        const string scenario = """
            public static class Scenario
            {
                public static int Run() => TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions.Count;
            }
            """;

        Assert.Equal(0, SourceGeneratorTestHost.Execute<int>(declaration, scenario));
    }

    [Fact]
    public void Catalog_is_sorted_by_key_regardless_of_file_order()
    {
        const string first = """
            namespace B;
            public class Flags : TinyFlags.IFeatureProvider { public bool Enabled => true; }
            """;
        const string second = """
            namespace A;
            public class Flags : TinyFlags.IFeatureProvider
            {
                public string Z => "last";
                public bool A => false;
            }
            """;
        const string scenario = """
            using System.Linq;
            public static class Scenario
            {
                public static string[] Run() => TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions
                    .Select(definition => definition.Key).ToArray();
            }
            """;

        var expected = new[] { "A.Flags.A", "A.Flags.Z", "B.Flags.Enabled" };
        Assert.Equal(expected, SourceGeneratorTestHost.Execute<string[]>(first, second, scenario));
        Assert.Equal(expected, SourceGeneratorTestHost.Execute<string[]>(second, first, scenario));
    }

    [Fact]
    public void Partial_declarations_contribute_each_flag_once()
    {
        const string first = "public partial class Flags : TinyFlags.IFeatureProvider { public bool Enabled => true; }";
        const string second = "public partial class Flags : TinyFlags.IFeatureProvider { public string Label => \"Buy\"; }";
        const string scenario = """
            using System.Linq;
            public static class Scenario
            {
                public static string[] Run() => TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions
                    .Select(definition => definition.Key).ToArray();
            }
            """;

        Assert.Equal(new[] { "Flags.Enabled", "Flags.Label" },
            SourceGeneratorTestHost.Execute<string[]>(first, second, scenario));
    }

    [Fact]
    public void Definitions_cannot_be_replaced_through_a_mutable_collection_interface()
    {
        const string declaration = "public class Flags : TinyFlags.IFeatureProvider { public bool Enabled => true; }";
        const string scenario = """
            public static class Scenario
            {
                public static bool Run()
                {
                    var definitions = TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions;
                    var mutable = (System.Collections.Generic.IList<TinyFlags.FeatureDefinition>)definitions;
                    try
                    {
                        mutable[0] = TinyFlags.FeatureDefinition.Boolean("Changed", false);
                        return false;
                    }
                    catch (System.NotSupportedException)
                    {
                        return definitions.Count == 1 && definitions[0].Key == "Flags.Enabled"
                            && (bool)definitions[0].DefaultValue;
                    }
                }
            }
            """;

        Assert.True(SourceGeneratorTestHost.Execute<bool>(declaration, scenario));
    }

    [Fact]
    public void Live_values_do_not_replace_declared_defaults_in_the_catalog()
    {
        const string source = """
            public class Flags : TinyFlags.IFeatureProvider { public bool Enabled => false; }
            public static class Scenario
            {
                public static bool[] Run()
                {
                    var values = new TinyFlags.FeatureValues();
                    values.ReplaceSnapshot(new System.Collections.Generic.Dictionary<string, object> { ["Flags.Enabled"] = true });
                    return new[]
                    {
                        new FlagsFeatureFlags(values).Enabled,
                        (bool)TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions[0].DefaultValue
                    };
                }
            }
            """;

        Assert.Equal(new[] { true, false }, SourceGeneratorTestHost.Execute<bool[]>(source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("quote\" slash\\ \n\t\0 Español\u2028\U0001f600")]
    public void Catalog_string_defaults_preserve_exact_values(string value)
    {
        var declaration = "public class Flags : TinyFlags.IFeatureProvider { public string Label => "
            + SymbolDisplay.FormatLiteral(value, quote: true) + "; }";
        const string scenario = """
            public static class Scenario
            {
                public static string Run() => (string)TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions[0].DefaultValue;
            }
            """;

        Assert.Equal(value, SourceGeneratorTestHost.Execute<string>(declaration, scenario));
    }

    [Theory]
    [InlineData("namespace TinyFlags.Generated { public class ThisAssemblyFeatureCatalog { } }")]
    [InlineData("namespace TinyFlags.Generated.ThisAssemblyFeatureCatalog { public class Existing { } }")]
    [InlineData("namespace TinyFlags { public class Generated { } }")]
    public void Reserved_catalog_name_conflicts_are_reported(string declaration)
    {
        var run = SourceGeneratorTestHost.Run(declaration);

        Assert.Equal("TFG005", Assert.Single(run.Diagnostics).Id);
        Assert.DoesNotContain(Assert.Single(run.Results).GeneratedSources,
            source => source.HintName == SourceGeneratorTestHost.CatalogHintName);
    }

    [Fact]
    public void Invalid_providers_are_excluded_from_the_catalog()
    {
        var run = SourceGeneratorTestHost.Run(
            "public class Broken : TinyFlags.IFeatureProvider { public int Value => 42; }",
            "public class Good : TinyFlags.IFeatureProvider { public bool Enabled => true; }");
        var source = Assert.Single(Assert.Single(run.Results).GeneratedSources,
            generated => generated.HintName == SourceGeneratorTestHost.CatalogHintName).SourceText.ToString();

        Assert.Equal("TFG002", Assert.Single(run.Diagnostics).Id);
        Assert.Contains("Good.Enabled", source);
        Assert.DoesNotContain("Broken.Value", source);
    }

    [Fact]
    public void Unrelated_edits_reuse_catalog_generation()
    {
        const string declaration = "public class Flags : TinyFlags.IFeatureProvider { public bool Enabled => true; }";
        const string helper = "public class Helper { public int Value => 1; }";
        var compilation = SourceGeneratorTestHost.CreateCompilation(declaration, helper);
        var driver = SourceGeneratorTestHost.Run(SourceGeneratorTestHost.CreateDriver(), compilation);
        var originalTree = compilation.SyntaxTrees.Last();
        var updated = compilation.ReplaceSyntaxTree(originalTree,
            CSharpSyntaxTree.ParseText(helper.Replace("1", "2"), path: originalTree.FilePath));
        var run = SourceGeneratorTestHost.Run(driver, updated).GetRunResult();

        Assert.Equal(IncrementalStepRunReason.Cached, CatalogGenerationReason(run));
    }

    [Fact]
    public void Removing_a_provider_updates_the_catalog_to_empty()
    {
        const string declaration = "public class Flags : TinyFlags.IFeatureProvider { public bool Enabled => true; }";
        var compilation = SourceGeneratorTestHost.CreateCompilation(declaration);
        var driver = SourceGeneratorTestHost.Run(SourceGeneratorTestHost.CreateDriver(), compilation);
        var originalTree = compilation.SyntaxTrees.Single();
        var updated = compilation.ReplaceSyntaxTree(originalTree,
            CSharpSyntaxTree.ParseText("public class Plain { }", path: originalTree.FilePath));
        var run = SourceGeneratorTestHost.Run(driver, updated).GetRunResult();

        Assert.Equal(IncrementalStepRunReason.Modified, CatalogGenerationReason(run));
        var source = Assert.Single(Assert.Single(run.Results).GeneratedSources,
            generated => generated.HintName == SourceGeneratorTestHost.CatalogHintName).SourceText.ToString();
        Assert.DoesNotContain("Flags.Enabled", source);
    }

    private static IncrementalStepRunReason CatalogGenerationReason(GeneratorDriverRunResult run)
    {
        return Assert.Single(Assert.Single(run.Results).TrackedSteps["FeatureCatalogGeneration"]
            .SelectMany(step => step.Outputs)).Reason;
    }
}
