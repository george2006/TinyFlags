using Microsoft.CodeAnalysis;

namespace TinyFlags.SourceGen.Tests;

public sealed class FeatureDiagnosticTests
{
    [Theory]
    [InlineData("public abstract class Checkout : TinyFlags.IFeatureProvider { public bool Flag => true; }")]
    [InlineData("public class Checkout<T> : TinyFlags.IFeatureProvider { public bool Flag => true; }")]
    [InlineData("public struct Checkout : TinyFlags.IFeatureProvider { public bool Flag => true; }")]
    [InlineData("public record Checkout : TinyFlags.IFeatureProvider { public bool Flag => true; }")]
    [InlineData("file class Checkout : TinyFlags.IFeatureProvider { public bool Flag => true; }")]
    [InlineData("public class Outer { public class Checkout : TinyFlags.IFeatureProvider { public bool Flag => true; } }")]
    [InlineData("public class Parent { public bool Inherited => true; } public class Checkout : Parent, TinyFlags.IFeatureProvider { }")]
    public void Reports_unsupported_provider_shapes(string source)
    {
        var diagnostic = Assert.Single(SourceGeneratorTestHost.Run(source).Diagnostics);

        Assert.Equal("TFG001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Checkout", SourceAt(diagnostic));
        Assert.Empty(SourceGeneratorTestHost.Discover(source).Providers);
    }

    [Theory]
    [InlineData("public int Flag => 42;")]
    [InlineData("public bool? Flag => true;")]
    [InlineData("public string? Flag => \"value\";")]
    [InlineData("public static bool Flag => true;")]
    [InlineData("private bool Flag => true;")]
    [InlineData("public bool Flag { get; set; } = true;")]
    [InlineData("public bool Flag { get; private set; } = true;")]
    [InlineData("public bool Flag { get; init; } = true;")]
    public void Reports_unsupported_property_shapes(string declaration)
    {
        var source = ProviderWith(declaration);
        var diagnostic = Assert.Single(SourceGeneratorTestHost.Run(source).Diagnostics);

        Assert.Equal("TFG002", diagnostic.Id);
        Assert.Equal("Flag", SourceAt(diagnostic));
        Assert.Empty(SourceGeneratorTestHost.Discover(source).Providers);
    }

    [Fact]
    public void Reports_indexers_as_unsupported_properties()
    {
        var diagnostic = Assert.Single(SourceGeneratorTestHost.Run(
            ProviderWith("public bool this[int index] => true;")).Diagnostics);

        Assert.Equal("TFG002", diagnostic.Id);
    }

    [Theory]
    [InlineData("public bool Flag => System.DateTime.UtcNow.Ticks > 0;")]
    [InlineData("public bool Flag { get; }")]
    [InlineData("public string Flag => null!;")]
    [InlineData("public string Flag => default!;")]
    [InlineData("public bool Flag => Read(); private static bool Read() => true;")]
    [InlineData("public bool Flag { get { System.Console.WriteLine(\"side effect\"); return true; } }")]
    public void Reports_defaults_that_require_execution_or_are_missing_or_null(string declaration)
    {
        var source = ProviderWith(declaration);
        var diagnostic = Assert.Single(SourceGeneratorTestHost.Run(source).Diagnostics);

        Assert.Equal("TFG003", diagnostic.Id);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Empty(SourceGeneratorTestHost.Discover(source).Providers);
    }

    [Fact]
    public void Reports_the_nonconstant_expression_at_its_source_location()
    {
        const string expression = "System.Environment.MachineName";
        var diagnostic = Assert.Single(SourceGeneratorTestHost.Run(
            ProviderWith("public string Flag => " + expression + ";")).Diagnostics);

        Assert.Equal("TFG003", diagnostic.Id);
        Assert.Equal(expression, SourceAt(diagnostic));
    }

    [Fact]
    public void Rejects_the_whole_provider_when_one_property_is_invalid()
    {
        const string invalid = """
            public class Broken : TinyFlags.IFeatureProvider
            {
                public bool Valid => true;
                public int Invalid => 42;
            }
            """;
        const string valid = """
            public class Good : TinyFlags.IFeatureProvider { public bool Flag => true; }
            """;

        var result = SourceGeneratorTestHost.Discover(invalid, valid);

        Assert.Equal("TFG002", Assert.Single(result.Diagnostics).Id);
        Assert.Equal("Good", Assert.Single(result.Providers).Name);
    }

    [Fact]
    public void Reports_each_invalid_property_once_across_partial_declarations()
    {
        const string first = """
            public partial class Checkout : TinyFlags.IFeatureProvider { public int First => 1; }
            """;
        const string second = """
            public partial class Checkout : TinyFlags.IFeatureProvider { public bool Second => bool.Parse("true"); }
            """;

        var diagnostics = SourceGeneratorTestHost.Run(first, second).Diagnostics;

        Assert.Equal(new[] { "TFG002", "TFG003" }, diagnostics.Select(diagnostic => diagnostic.Id));
    }

    private static string ProviderWith(string declaration)
    {
        return "using TinyFlags; public class Checkout : IFeatureProvider { " + declaration + " }";
    }

    private static string SourceAt(Diagnostic diagnostic)
    {
        return diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);
    }
}
