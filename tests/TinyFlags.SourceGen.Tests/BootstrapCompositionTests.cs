namespace TinyFlags.SourceGen.Tests;

public sealed class BootstrapCompositionTests
{
    [Fact]
    public void Caller_mutations_cannot_change_registered_definitions()
    {
        const string body = """
            var supplied = new[] { FeatureDefinition.Boolean("Flags.Enabled", true) };
            TinyFlagsBootstrap.AddContribution(typeof(Scenario), supplied);
            supplied[0] = FeatureDefinition.Boolean("Changed", false);
            var definition = TinyFlagsBootstrap.GetDefinitions().Single();
            return definition.Key == "Flags.Enabled" && (bool)definition.DefaultValue;
            """;

        Assert.True(Execute<bool>(body));
    }

    [Fact]
    public void Earlier_snapshots_remain_immutable_when_new_contributions_arrive()
    {
        const string body = """
            TinyFlagsBootstrap.AddContribution(typeof(int), new[] { FeatureDefinition.Boolean("A", true) });
            var before = TinyFlagsBootstrap.GetDefinitions();
            TinyFlagsBootstrap.AddContribution(typeof(string), new[] { FeatureDefinition.String("B", "Buy") });
            var after = TinyFlagsBootstrap.GetDefinitions();
            try
            {
                ((IList<FeatureDefinition>)after)[0] = FeatureDefinition.Boolean("Changed", false);
                return false;
            }
            catch (NotSupportedException)
            {
                return before.Count == 1 && before[0].Key == "A"
                    && after.Count == 2 && after[0].Key == "A" && after[1].Key == "B";
            }
            """;

        Assert.True(Execute<bool>(body));
    }

    [Fact]
    public void An_invalid_contribution_does_not_partially_register()
    {
        const string body = """
            try
            {
                TinyFlagsBootstrap.AddContribution(typeof(Scenario),
                    new[] { FeatureDefinition.Boolean("Invalid", true), null! });
                return false;
            }
            catch (ArgumentException error)
            {
                if (error.ParamName != "definitions") return false;
            }

            TinyFlagsBootstrap.AddContribution(typeof(Scenario), new[] { FeatureDefinition.Boolean("Valid", false) });
            return TinyFlagsBootstrap.GetDefinitions().Single().Key == "Valid";
            """;

        Assert.True(Execute<bool>(body));
    }

    [Theory]
    [InlineData("null!, Array.Empty<FeatureDefinition>()", "contributionType")]
    [InlineData("typeof(Scenario), null!", "definitions")]
    public void Missing_registration_arguments_are_rejected(string arguments, string parameterName)
    {
        var body = "try { TinyFlagsBootstrap.AddContribution(" + arguments + "); return \"accepted\"; }"
            + "catch (ArgumentNullException error) { return error.ParamName!; }";

        Assert.Equal(parameterName, Execute<string>(body));
    }

    [Fact]
    public void Composition_keeps_case_sensitive_keys_distinct()
    {
        const string body = """
            TinyFlagsBootstrap.AddContribution(typeof(Scenario), new[]
            {
                FeatureDefinition.Boolean("Flags.Enabled", true),
                FeatureDefinition.Boolean("flags.Enabled", false)
            });
            return TinyFlagsBootstrap.GetDefinitions().Select(definition => definition.Key).ToArray();
            """;

        Assert.Equal(new[] { "Flags.Enabled", "flags.Enabled" }, Execute<string[]>(body));
    }

    [Fact]
    public void Concurrent_registration_and_composition_only_expose_complete_contributions()
    {
        const string body = """
            var owners = new[] { typeof(int), typeof(string), typeof(bool), typeof(byte) };
            System.Threading.Tasks.Parallel.For(0, 200, index =>
            {
                var owner = owners[index % owners.Length];
                TinyFlagsBootstrap.AddContribution(owner, new[]
                {
                    FeatureDefinition.Boolean(owner.Name + ".A", true),
                    FeatureDefinition.String(owner.Name + ".B", "Buy")
                });
                var snapshot = TinyFlagsBootstrap.GetDefinitions();
                if (snapshot.Count % 2 != 0) throw new InvalidOperationException("Partial contribution");
            });
            return TinyFlagsBootstrap.GetDefinitions().Count;
            """;

        Assert.Equal(8, Execute<int>(body));
    }

    private static T Execute<T>(string body)
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using TinyFlags;
            public static class Scenario
            {
                public static object Run()
                {
            """ + body + """
                }
            }
            """;
        return SourceGeneratorTestHost.Execute<T>(source);
    }
}
