using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TinyFlags.SourceGen.Tests;

public sealed class GeneratedRegistrationTests
{
    [Fact]
    public void Partial_and_empty_providers_with_escaped_names_resolve_once()
    {
        const string declarations = """
            namespace @namespace
            {
                internal partial class @class : TinyFlags.IFeatureProvider { public bool Enabled => true; }
                internal partial class @class { public string Label => "Buy"; }
                internal class Empty : TinyFlags.IFeatureProvider { }
            }
            """;
        const string scenario = """
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TinyFlags;
            public static class Scenario
            {
                public static bool Run()
                {
                    using var provider = new ServiceCollection().AddTinyFlags().BuildServiceProvider();
                    var flags = provider.GetRequiredService<@namespace.classFeatureFlags>();
                    return flags.Enabled && flags.Label == "Buy"
                        && provider.GetServices<@namespace.classFeatureFlags>().Count() == 1
                        && provider.GetServices<@namespace.EmptyFeatureFlags>().Count() == 1;
                }
            }
            """;

        Assert.True(SourceGeneratorTestHost.Execute<bool>(declarations, scenario));
    }

    [Fact]
    public void Generated_registration_preserves_an_explicit_access_instance()
    {
        const string source = """
            using System;
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using TinyFlags;
            public class Checkout : IFeatureProvider { public bool Enabled => false; }
            public static class Scenario
            {
                public static bool Run()
                {
                    var custom = new CheckoutFeatureFlags(new FeatureValues());
                    var services = new ServiceCollection();
                    services.AddSingleton(custom);
                    services.AddTinyFlags();
                    services.AddTinyFlags();
                    using var provider = services.BuildServiceProvider();
                    return ReferenceEquals(custom, provider.GetRequiredService<CheckoutFeatureFlags>())
                        && provider.GetServices<CheckoutFeatureFlags>().Count() == 1;
                }
            }
            """;

        Assert.True(SourceGeneratorTestHost.Execute<bool>(source));
    }

    [Fact]
    public void Renaming_an_empty_provider_invalidates_its_registration_plan()
    {
        const string declaration = "public class Before : TinyFlags.IFeatureProvider { }";
        var compilation = SourceGeneratorTestHost.CreateCompilation(declaration);
        var driver = SourceGeneratorTestHost.Run(SourceGeneratorTestHost.CreateDriver(), compilation);
        var tree = compilation.SyntaxTrees.Single();
        var updated = compilation.ReplaceSyntaxTree(tree,
            CSharpSyntaxTree.ParseText(declaration.Replace("Before", "After"), path: tree.FilePath));
        var run = SourceGeneratorTestHost.Run(driver, updated).GetRunResult();

        Assert.Equal(IncrementalStepRunReason.Modified,
            Assert.Single(Assert.Single(run.Results).TrackedSteps["FeatureCatalogGeneration"]
                .SelectMany(step => step.Outputs)).Reason);
        var source = Assert.Single(Assert.Single(run.Results).GeneratedSources,
            item => item.HintName == SourceGeneratorTestHost.CatalogHintName).SourceText.ToString();
        Assert.Contains("global::AfterFeatureFlags", source);
        Assert.DoesNotContain("BeforeFeatureFlags", source);
    }

    [Fact]
    public void Invalid_providers_do_not_contribute_service_registrations()
    {
        var run = SourceGeneratorTestHost.Run("""
            public class Broken : TinyFlags.IFeatureProvider { public int Flag => 1; }
            public class Good : TinyFlags.IFeatureProvider { public bool Flag => true; }
            """);
        var source = Assert.Single(Assert.Single(run.Results).GeneratedSources,
            item => item.HintName == SourceGeneratorTestHost.CatalogHintName).SourceText.ToString();

        Assert.Equal("TFG002", Assert.Single(run.Diagnostics).Id);
        Assert.Contains("global::GoodFeatureFlags", source);
        Assert.DoesNotContain("BrokenFeatureFlags", source);
    }
}
