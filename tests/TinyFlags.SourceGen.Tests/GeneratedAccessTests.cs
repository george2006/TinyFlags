using Microsoft.CodeAnalysis.CSharp;

namespace TinyFlags.SourceGen.Tests;

public sealed class GeneratedAccessTests
{
    [Fact]
    public void Generated_properties_return_typed_defaults_without_constructing_the_declaration()
    {
        const string source = """
            public class Checkout : TinyFlags.IFeatureProvider
            {
                public Checkout() => throw new System.InvalidOperationException();
                public bool Enabled => false;
                public string Label => "Comprar";
            }
            public static class Scenario
            {
                public static object[] Run()
                {
                    var flags = new CheckoutFeatureFlags(new TinyFlags.FeatureValues());
                    bool enabled = flags.Enabled;
                    string label = flags.Label;
                    return new object[] { enabled, label, (object)flags is TinyFlags.IFeatureProvider };
                }
            }
            """;

        Assert.Equal(new object[] { false, "Comprar", false }, SourceGeneratorTestHost.Execute<object[]>(source));
    }

    [Fact]
    public void Existing_generated_instance_observes_replacements_and_returns_to_defaults()
    {
        const string source = """
            public class Checkout : TinyFlags.IFeatureProvider
            {
                public bool Enabled => false;
                public string Label => "Comprar";
            }
            public static class Scenario
            {
                public static object[] Run()
                {
                    var values = new TinyFlags.FeatureValues();
                    var flags = new CheckoutFeatureFlags(values);
                    var before = flags.Label;
                    values.ReplaceSnapshot(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["Checkout.Enabled"] = true,
                        ["Checkout.Label"] = "Buy"
                    });
                    var enabled = flags.Enabled;
                    var updated = flags.Label;
                    values.ReplaceSnapshot(new System.Collections.Generic.Dictionary<string, object>());
                    return new object[] { before, enabled, updated, flags.Enabled, flags.Label };
                }
            }
            """;

        Assert.Equal(new object[] { "Comprar", true, "Buy", false, "Comprar" },
            SourceGeneratorTestHost.Execute<object[]>(source));
    }

    [Fact]
    public void Providers_with_the_same_name_in_different_namespaces_read_distinct_keys()
    {
        const string first = """
            namespace A;
            public class Checkout : TinyFlags.IFeatureProvider { public bool Enabled => true; }
            """;
        const string second = """
            namespace B;
            public class Checkout : TinyFlags.IFeatureProvider { public bool Enabled => false; }
            """;
        const string consumer = """
            public static class Scenario
            {
                public static bool[] Run()
                {
                    var values = new TinyFlags.FeatureValues();
                    values.ReplaceSnapshot(new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["A.Checkout.Enabled"] = false,
                        ["B.Checkout.Enabled"] = true
                    });
                    return new[] { new A.CheckoutFeatureFlags(values).Enabled, new B.CheckoutFeatureFlags(values).Enabled };
                }
            }
            """;

        Assert.Equal(new[] { false, true }, SourceGeneratorTestHost.Execute<bool[]>(first, second, consumer));
    }

    [Fact]
    public void Partial_declarations_produce_one_usable_class()
    {
        const string first = """
            public partial class Checkout : TinyFlags.IFeatureProvider { public bool Enabled => true; }
            """;
        const string second = """
            public partial class Checkout : TinyFlags.IFeatureProvider { public string Label => "Buy"; }
            """;
        const string consumer = """
            public static class Scenario
            {
                public static object[] Run()
                {
                    var flags = new CheckoutFeatureFlags(new TinyFlags.FeatureValues());
                    return new object[] { flags.Enabled, flags.Label };
                }
            }
            """;

        Assert.Equal(new object[] { true, "Buy" }, SourceGeneratorTestHost.Execute<object[]>(first, second, consumer));
        Assert.Single(Assert.Single(SourceGeneratorTestHost.Run(first, second).Results).GeneratedSources);
    }

    [Fact]
    public void Escaped_identifiers_compile_in_namespaces_and_properties()
    {
        const string source = """
            namespace @event
            {
                public class @class : TinyFlags.IFeatureProvider
                {
                    public string @new => "Buy";
                    public bool @return => true;
                }
            }
            public static class Scenario
            {
                public static object[] Run()
                {
                    var flags = new @event.classFeatureFlags(new TinyFlags.FeatureValues());
                    return new object[] { flags.@new, flags.@return };
                }
            }
            """;

        Assert.Equal(new object[] { "Buy", true }, SourceGeneratorTestHost.Execute<object[]>(source));
    }

    [Fact]
    public void Properties_cannot_collide_with_the_generated_backing_field()
    {
        const string source = """
            public class Checkout : TinyFlags.IFeatureProvider
            {
                public bool _values => true;
                public bool _values_ => false;
                public string values => "Buy";
            }
            public static class Scenario
            {
                public static object[] Run()
                {
                    var flags = new CheckoutFeatureFlags(new TinyFlags.FeatureValues());
                    return new object[] { flags._values, flags._values_, flags.values };
                }
            }
            """;

        Assert.Equal(new object[] { true, false, "Buy" }, SourceGeneratorTestHost.Execute<object[]>(source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Quote \" slash \\ newline\nreturn\rtab\tzero\0")]
    [InlineData("Español \u2028\u2029 \U0001f600")]
    [InlineData("\u007f\u0085")]
    public void String_defaults_survive_source_emission_exactly(string value)
    {
        var declaration = "public class Checkout : TinyFlags.IFeatureProvider { public string Label => "
            + SymbolDisplay.FormatLiteral(value, quote: true) + "; }";
        const string consumer = """
            public static class Scenario
            {
                public static string Run() => new CheckoutFeatureFlags(new TinyFlags.FeatureValues()).Label;
            }
            """;

        Assert.Equal(value, SourceGeneratorTestHost.Execute<string>(declaration, consumer));
    }

    [Theory]
    [InlineData("ToString")]
    [InlineData("Equals")]
    [InlineData("GetType")]
    public void Properties_can_hide_object_members(string property)
    {
        var declaration = "public class Checkout : TinyFlags.IFeatureProvider { public new bool " + property + " => true; }";
        var consumer = "public static class Scenario { public static bool Run() => "
            + "new CheckoutFeatureFlags(new TinyFlags.FeatureValues())." + property + "; }";

        Assert.True(SourceGeneratorTestHost.Execute<bool>(declaration, consumer));
    }

    [Fact]
    public void Generated_constructor_rejects_a_missing_value_store()
    {
        const string source = """
            public class Checkout : TinyFlags.IFeatureProvider { }
            public static class Scenario
            {
                public static bool Run()
                {
                    try { _ = new CheckoutFeatureFlags(null!); }
                    catch (System.ArgumentNullException error) { return error.ParamName == "values"; }
                    return false;
                }
            }
            """;

        Assert.True(SourceGeneratorTestHost.Execute<bool>(source));
    }

    [Theory]
    [InlineData("public class CheckoutFeatureFlags { }")]
    [InlineData("namespace CheckoutFeatureFlags { public class Existing { } }")]
    public void Existing_declarations_report_a_name_conflict_without_emitting_a_class(string existing)
    {
        var run = SourceGeneratorTestHost.Run(
            "public class Checkout : TinyFlags.IFeatureProvider { public bool Enabled => true; }", existing);

        Assert.Equal("TFG004", Assert.Single(run.Diagnostics).Id);
        Assert.Empty(Assert.Single(run.Results).GeneratedSources);
    }

    [Theory]
    [InlineData("Shop")]
    [InlineData("@event")]
    public void Existing_types_in_the_provider_namespace_report_a_name_conflict(string namespaceName)
    {
        var run = SourceGeneratorTestHost.Run("namespace " + namespaceName
            + " { public class Checkout : TinyFlags.IFeatureProvider { public bool Enabled => true; }"
            + " public class CheckoutFeatureFlags { } }");

        Assert.Equal("TFG004", Assert.Single(run.Diagnostics).Id);
        Assert.Empty(Assert.Single(run.Results).GeneratedSources);
    }

    [Fact]
    public void Feature_named_like_its_generated_class_reports_a_name_conflict()
    {
        var run = SourceGeneratorTestHost.Run(
            "public class Checkout : TinyFlags.IFeatureProvider { public bool CheckoutFeatureFlags => true; }");

        Assert.Equal("TFG004", Assert.Single(run.Diagnostics).Id);
        Assert.Empty(Assert.Single(run.Results).GeneratedSources);
    }

    [Fact]
    public void Invalid_provider_does_not_prevent_other_providers_from_generating()
    {
        var run = SourceGeneratorTestHost.Run(
            "public class Broken : TinyFlags.IFeatureProvider { public int Flag => 42; }",
            "public class Good : TinyFlags.IFeatureProvider { public bool Flag => true; }");

        Assert.Equal("TFG002", Assert.Single(run.Diagnostics).Id);
        Assert.Equal("GoodFeatureFlags.g.cs", Assert.Single(Assert.Single(run.Results).GeneratedSources).HintName);
    }
}
