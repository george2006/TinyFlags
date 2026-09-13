using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Tests;

public sealed class FeatureDeclarationTests
{
    [Fact]
    public void Discovers_boolean_and_string_defaults_without_executing_the_provider()
    {
        const string source = """
            using TinyFlags;
            namespace Shop;

            public sealed class Checkout : IFeatureProvider
            {
                public Checkout() => throw new System.InvalidOperationException();
                public bool NuevoCheckout => false;
                public string TextoBoton => "Comprar";
            }
            """;

        var result = SourceGeneratorTestHost.Discover(source);

        Assert.Empty(result.Diagnostics);
        var provider = Assert.Single(result.Providers);
        Assert.Equal("Checkout", provider.Name);
        Assert.Equal("Shop", provider.NamespaceName);
        Assert.Equal("Shop.Checkout", provider.QualifiedName);
        Assert.Collection(provider.Features,
            flag =>
            {
                Assert.Equal("NuevoCheckout", flag.Name);
                Assert.Equal("Shop.Checkout.NuevoCheckout", flag.Key);
                Assert.Equal(FeatureValueKind.Boolean, flag.Kind);
                Assert.False(Assert.IsType<bool>(flag.DefaultValue));
            },
            flag =>
            {
                Assert.Equal("TextoBoton", flag.Name);
                Assert.Equal("Shop.Checkout.TextoBoton", flag.Key);
                Assert.Equal(FeatureValueKind.String, flag.Kind);
                Assert.Equal("Comprar", Assert.IsType<string>(flag.DefaultValue));
            });
        Assert.Empty(SourceGeneratorTestHost.Run(source).Diagnostics);
    }

    [Theory]
    [InlineData("public bool Flag => true;", true)]
    [InlineData("public bool Flag { get; } = false;", false)]
    [InlineData("public bool Flag { get => true; }", true)]
    [InlineData("public bool Flag { get { return false; } }", false)]
    [InlineData("private const bool Default = true; public bool Flag => Default;", true)]
    [InlineData("public bool Flag => 1 < 2;", true)]
    public void Reads_constant_boolean_defaults(string declaration, bool expected)
    {
        var result = SourceGeneratorTestHost.Discover(ProviderWith(declaration));

        Assert.Empty(result.Diagnostics);
        var flag = Assert.Single(Assert.Single(result.Providers).Features);
        Assert.Equal(expected, Assert.IsType<bool>(flag.DefaultValue));
    }

    [Theory]
    [InlineData("public string Flag => \"\";", "")]
    [InlineData("public string Flag { get; } = \"Comprar\";", "Comprar")]
    [InlineData("public string Flag => nameof(Checkout);", "Checkout")]
    [InlineData("private const string Label = \"Com\"; public string Flag => Label + \"prar\";", "Comprar")]
    [InlineData("public string Flag => \"línea\\n\\\"dos\\\"\";", "línea\n\"dos\"")]
    public void Reads_constant_string_defaults(string declaration, string expected)
    {
        var result = SourceGeneratorTestHost.Discover(ProviderWith(declaration));

        Assert.Empty(result.Diagnostics);
        var flag = Assert.Single(Assert.Single(result.Providers).Features);
        Assert.Equal(expected, Assert.IsType<string>(flag.DefaultValue));
    }

    [Fact]
    public void Ignores_unmarked_classes_and_unrelated_interfaces_with_the_same_name()
    {
        const string source = """
            namespace Other;
            public interface IFeatureProvider { }
            public class Plain { public bool Flag => true; }
            public class Impostor : IFeatureProvider { public int Flag => 42; }
            """;

        var result = SourceGeneratorTestHost.Discover(source);

        Assert.Empty(result.Providers);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(SourceGeneratorTestHost.Run(source).Diagnostics);
    }

    [Fact]
    public void Recognizes_the_marker_through_a_using_alias()
    {
        const string source = """
            using Marker = TinyFlags.IFeatureProvider;
            public class Checkout : Marker { public bool Flag => true; }
            """;

        var result = SourceGeneratorTestHost.Discover(source);

        Assert.Empty(result.Diagnostics);
        var provider = Assert.Single(result.Providers);
        Assert.Equal(string.Empty, provider.NamespaceName);
        Assert.Equal("Checkout.Flag", Assert.Single(provider.Features).Key);
        Assert.Empty(SourceGeneratorTestHost.Run(source).Diagnostics);
    }

    [Fact]
    public void Combines_partial_declarations_into_one_provider()
    {
        const string first = """
            using TinyFlags;
            public partial class Checkout : IFeatureProvider { public bool Enabled => true; }
            """;
        const string second = """
            public partial class Checkout { public string Label => "Comprar"; }
            """;

        var result = SourceGeneratorTestHost.Discover(first, second);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, Assert.Single(result.Providers).Features.Length);
        Assert.Empty(SourceGeneratorTestHost.Run(first, second).Diagnostics);
    }

    [Fact]
    public void Qualifies_keys_independently_of_source_file_order()
    {
        const string first = """
            namespace B;
            public class Checkout : TinyFlags.IFeatureProvider { public bool Enabled => true; }
            """;
        const string second = """
            namespace A;
            public class Checkout : TinyFlags.IFeatureProvider { public bool Enabled => false; }
            """;

        var forward = SourceGeneratorTestHost.Discover(first, second);
        var reverse = SourceGeneratorTestHost.Discover(second, first);

        var expected = new[] { "A.Checkout.Enabled", "B.Checkout.Enabled" };
        Assert.Equal(expected, forward.Providers.SelectMany(provider => provider.Features)
            .Select(flag => flag.Key).OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(expected, reverse.Providers.SelectMany(provider => provider.Features)
            .Select(flag => flag.Key).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void Empty_provider_has_no_features_or_diagnostics()
    {
        var result = SourceGeneratorTestHost.Discover(ProviderWith(string.Empty));

        Assert.Empty(result.Diagnostics);
        Assert.Empty(Assert.Single(result.Providers).Features);
    }

    private static string ProviderWith(string declaration)
    {
        return "using TinyFlags; public class Checkout : IFeatureProvider { " + declaration + " }";
    }
}
