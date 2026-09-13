using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TinyFlags.SourceGen.Tests;

public sealed class IncrementalGenerationTests
{
    private const string Checkout = """
        public class Checkout : TinyFlags.IFeatureProvider { public bool Enabled => true; }
        """;
    private const string Search = """
        public class Search : TinyFlags.IFeatureProvider { public string Label => "Find"; }
        """;

    [Fact]
    public void Unrelated_edit_reuses_validation_for_all_providers()
    {
        const string unrelated = "public class Helper { public int Value => 1; }";
        var run = RunEdit(new[] { Checkout, Search, unrelated }, 2, unrelated.Replace("1", "2"));

        Assert.All(Reasons(run, "FeatureAnalysis"), reason =>
            Assert.Contains(reason, new[] { IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged }));
        Assert.Equal(new[] { IncrementalStepRunReason.Cached, IncrementalStepRunReason.Cached },
            Reasons(run, "FeatureValidation"));
        Assert.Equal(2, SourceGeneratorTestHost.ReadDefinitions(run).Providers.Length);
    }

    [Fact]
    public void Changing_one_default_only_revalidates_that_provider()
    {
        var run = RunEdit(new[] { Checkout, Search }, 0, Checkout.Replace("true", "false"));

        Assert.Equal(new[] { IncrementalStepRunReason.Modified, IncrementalStepRunReason.Cached },
            Reasons(run, "FeatureValidation"));
        var providers = SourceGeneratorTestHost.ReadDefinitions(run).Providers;
        Assert.False((bool)Assert.Single(providers[0].Features).DefaultValue);
        Assert.Equal("Find", Assert.Single(providers[1].Features).DefaultValue);
    }

    [Fact]
    public void Equivalent_declaration_reuses_validation_after_its_syntax_changes()
    {
        const string original = """
            public class Checkout : TinyFlags.IFeatureProvider
            {
                public bool Enabled => true;
                public string Label => "Buy";
                public void Helper() { System.Console.WriteLine("one"); }
            }
            """;
        var run = RunEdit(new[] { original }, 0, original.Replace("one", "two"));

        Assert.Equal(IncrementalStepRunReason.Unchanged, Assert.Single(Reasons(run, "FeatureAnalysis")));
        Assert.Equal(IncrementalStepRunReason.Cached, Assert.Single(Reasons(run, "FeatureValidation")));
    }

    [Fact]
    public void External_constant_changes_update_its_consuming_provider()
    {
        const string constants = "public static class Defaults { public const bool Enabled = true; }";
        var provider = Checkout.Replace("=> true", "=> Defaults.Enabled");
        var run = RunEdit(new[] { provider, Search, constants }, 2, constants.Replace("true", "false"));

        Assert.Equal(new[] { IncrementalStepRunReason.Modified, IncrementalStepRunReason.Cached },
            Reasons(run, "FeatureValidation"));
        Assert.False((bool)Assert.Single(SourceGeneratorTestHost.ReadDefinitions(run).Providers[0].Features).DefaultValue);
    }

    [Fact]
    public void Editing_a_partial_without_the_marker_updates_the_single_provider()
    {
        const string first = "public partial class Checkout : TinyFlags.IFeatureProvider { }";
        const string second = "public partial class Checkout { public bool Enabled => true; }";
        var run = RunEdit(new[] { first, second }, 1, second.Replace("true", "false"));

        Assert.Equal(IncrementalStepRunReason.Modified, Assert.Single(Reasons(run, "FeatureValidation")));
        var provider = Assert.Single(SourceGeneratorTestHost.ReadDefinitions(run).Providers);
        Assert.False((bool)Assert.Single(provider.Features).DefaultValue);
    }

    [Fact]
    public void Moving_an_invalid_declaration_updates_the_diagnostic_location()
    {
        var invalid = Checkout.Replace("bool Enabled => true", "int Enabled => 42");
        var edited = "\n\n" + invalid;
        var run = RunEdit(new[] { invalid }, 0, edited);
        var diagnostic = Assert.Single(run.Diagnostics);

        Assert.Equal("TFG002", diagnostic.Id);
        Assert.Equal(edited.IndexOf("Enabled", StringComparison.Ordinal), diagnostic.Location.SourceSpan.Start);
        Assert.Equal(edited, diagnostic.Location.SourceTree!.GetText().ToString());
        Assert.Equal(IncrementalStepRunReason.Modified, Assert.Single(Reasons(run, "FeatureValidation")));
    }

    [Fact]
    public void Removing_the_marker_removes_the_provider_and_its_diagnostic()
    {
        var invalid = Checkout.Replace("bool Enabled => true", "int Enabled => 42");
        var run = RunEdit(new[] { invalid, Search }, 0, invalid.Replace(" : TinyFlags.IFeatureProvider", ""));

        Assert.Empty(run.Diagnostics);
        Assert.Equal("Search", Assert.Single(SourceGeneratorTestHost.ReadDefinitions(run).Providers).Name);
        Assert.Contains(IncrementalStepRunReason.Removed, Reasons(run, "FeatureValidation"));
    }

    [Fact]
    public void Fixing_an_invalid_property_removes_its_diagnostic_and_restores_definitions()
    {
        var invalid = Checkout.Replace("bool Enabled => true", "int Enabled => 42");
        var run = RunEdit(new[] { invalid }, 0, Checkout);

        Assert.Empty(run.Diagnostics);
        Assert.True((bool)Assert.Single(Assert.Single(
            SourceGeneratorTestHost.ReadDefinitions(run).Providers).Features).DefaultValue);
        Assert.Equal(IncrementalStepRunReason.Modified, Assert.Single(Reasons(run, "FeatureValidation")));
    }

    [Fact]
    public void Moving_a_valid_declaration_preserves_the_validated_definition()
    {
        var run = RunEdit(new[] { Checkout }, 0, "\n" + Checkout);

        Assert.Equal(IncrementalStepRunReason.Modified, Assert.Single(Reasons(run, "FeatureAnalysis")));
        Assert.Equal(IncrementalStepRunReason.Unchanged, Assert.Single(Reasons(run, "FeatureValidation")));
        Assert.True((bool)Assert.Single(Assert.Single(
            SourceGeneratorTestHost.ReadDefinitions(run).Providers).Features).DefaultValue);
    }

    [Fact]
    public void Cached_diagnostics_are_bound_to_the_updated_syntax_tree()
    {
        const string original = """
            public class Checkout : TinyFlags.IFeatureProvider
            {
                public int Enabled => 42;
                public void Helper() { System.Console.WriteLine("one"); }
            }
            """;
        var edited = original.Replace("one", "two");
        var run = RunEdit(new[] { original }, 0, edited);
        var diagnostic = Assert.Single(run.Diagnostics);

        Assert.Equal(IncrementalStepRunReason.Cached, Assert.Single(Reasons(run, "FeatureValidation")));
        Assert.Equal("TFG002", diagnostic.Id);
        Assert.Equal(edited, diagnostic.Location.SourceTree!.GetText().ToString());
    }

    [Fact]
    public void Repeated_markers_on_partial_declarations_do_not_duplicate_a_provider()
    {
        const string first = "public partial class Checkout : TinyFlags.IFeatureProvider { }";
        const string second = """
            public partial class Checkout : TinyFlags.IFeatureProvider { public bool Enabled => true; }
            """;
        var run = RunEdit(new[] { first, second }, 1, second.Replace("true", "false"));

        Assert.Single(Reasons(run, "FeatureValidation"));
        Assert.False((bool)Assert.Single(Assert.Single(
            SourceGeneratorTestHost.ReadDefinitions(run).Providers).Features).DefaultValue);
    }

    [Fact]
    public void Removing_the_first_partial_keeps_the_remaining_provider()
    {
        const string first = "public partial class Checkout : TinyFlags.IFeatureProvider { }";
        const string second = """
            public partial class Checkout : TinyFlags.IFeatureProvider { public bool Enabled => true; }
            """;
        var run = RunEdit(new[] { first, second }, 0, "");

        Assert.Empty(run.Diagnostics);
        Assert.True((bool)Assert.Single(Assert.Single(
            SourceGeneratorTestHost.ReadDefinitions(run).Providers).Features).DefaultValue);
    }

    private static GeneratorDriverRunResult RunEdit(string[] sources, int editedIndex, string editedSource)
    {
        var compilation = SourceGeneratorTestHost.CreateCompilation(sources);
        var driver = SourceGeneratorTestHost.Run(SourceGeneratorTestHost.CreateDriver(), compilation);
        var originalTree = compilation.SyntaxTrees.ElementAt(editedIndex);
        var editedTree = CSharpSyntaxTree.ParseText(editedSource, path: originalTree.FilePath);

        var updated = compilation.ReplaceSyntaxTree(originalTree, editedTree);
        return SourceGeneratorTestHost.Run(driver, updated).GetRunResult();
    }

    private static IncrementalStepRunReason[] Reasons(GeneratorDriverRunResult run, string stepName)
    {
        return Assert.Single(run.Results).TrackedSteps[stepName]
            .SelectMany(step => step.Outputs).Select(output => output.Reason).ToArray();
    }
}
