namespace TinyFlags.SourceGen.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void Repeated_registration_resolves_one_store_and_one_access_instance_across_scopes()
    {
        const string body = """
            RegisterFlags();
            var services = new ServiceCollection();
            var returned = services.AddTinyFlags();
            services.AddTinyFlags();
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using var scope = provider.CreateScope();
            var values = provider.GetRequiredService<FeatureValues>();
            var flags = provider.GetRequiredService<CheckoutConsumer>();
            var wasDisabled = !flags.Enabled;
            values.ReplaceSnapshot(new Dictionary<string, object> { ["Checkout.Enabled"] = true });
            return ReferenceEquals(returned, services)
                && provider.GetServices<FeatureValues>().Count() == 1
                && provider.GetServices<CheckoutConsumer>().Count() == 1
                && ReferenceEquals(values, scope.ServiceProvider.GetRequiredService<FeatureValues>())
                && ReferenceEquals(flags, scope.ServiceProvider.GetRequiredService<CheckoutConsumer>())
                && wasDisabled && flags.Enabled;
            """;

        Assert.True(Execute<bool>(body));
    }

    [Fact]
    public void Separate_containers_have_independent_stores_and_access_instances()
    {
        const string body = """
            RegisterFlags();
            var services = new ServiceCollection().AddTinyFlags();
            using var first = services.BuildServiceProvider();
            using var second = services.BuildServiceProvider();
            var firstFlags = first.GetRequiredService<CheckoutConsumer>();
            var secondFlags = second.GetRequiredService<CheckoutConsumer>();
            first.GetRequiredService<FeatureValues>().ReplaceSnapshot(
                new Dictionary<string, object> { ["Checkout.Enabled"] = true });
            return !ReferenceEquals(firstFlags, secondFlags)
                && firstFlags.Enabled && !secondFlags.Enabled;
            """;

        Assert.True(Execute<bool>(body));
    }

    [Fact]
    public void Explicit_store_is_preserved_and_used_by_registered_flags()
    {
        const string body = """
            RegisterFlags();
            var values = new FeatureValues();
            values.ReplaceSnapshot(new Dictionary<string, object> { ["Checkout.Enabled"] = true });
            var services = new ServiceCollection();
            services.AddSingleton(values);
            services.AddTinyFlags();
            using var provider = services.BuildServiceProvider();
            return ReferenceEquals(values, provider.GetRequiredService<FeatureValues>())
                && provider.GetRequiredService<CheckoutConsumer>().Enabled;
            """;

        Assert.True(Execute<bool>(body));
    }

    [Fact]
    public void First_contribution_keeps_its_definitions_and_registration_together()
    {
        const string body = """
            RegisterFlags();
            TinyFlagsBootstrap.AddContribution(typeof(Scenario),
                new[] { FeatureDefinition.Boolean("Unexpected", true) },
                services => services.AddSingleton("Unexpected"));
            using var provider = new ServiceCollection().AddTinyFlags().BuildServiceProvider();
            return provider.GetService<CheckoutConsumer>() is not null
                && provider.GetService<string>() is null
                && TinyFlagsBootstrap.GetDefinitions().Single().Key == "Checkout.Enabled";
            """;

        Assert.True(Execute<bool>(body));
    }

    [Fact]
    public void Metadata_only_registration_cannot_be_replaced_by_a_later_callback()
    {
        const string body = """
            TinyFlagsBootstrap.AddContribution(typeof(Scenario), Array.Empty<FeatureDefinition>());
            RegisterFlags();
            using var provider = new ServiceCollection().AddTinyFlags().BuildServiceProvider();
            return provider.GetService<CheckoutConsumer>() is null
                && provider.GetService<FeatureValues>() is not null;
            """;

        Assert.True(Execute<bool>(body));
    }

    [Fact]
    public void Later_contributions_can_be_applied_before_building_the_container()
    {
        const string body = """
            var services = new ServiceCollection().AddTinyFlags();
            RegisterFlags();
            services.AddTinyFlags();
            using var provider = services.BuildServiceProvider();
            return provider.GetService<CheckoutConsumer>() is not null
                && provider.GetServices<FeatureValues>().Count() == 1;
            """;

        Assert.True(Execute<bool>(body));
    }

    [Fact]
    public void Rejected_null_callback_does_not_reserve_the_contribution_identity()
    {
        const string body = """
            try
            {
                TinyFlagsBootstrap.AddContribution(typeof(Scenario), Array.Empty<FeatureDefinition>(), null!);
                return false;
            }
            catch (ArgumentNullException error)
            {
                if (error.ParamName != "register") return false;
            }
            RegisterFlags();
            using var provider = new ServiceCollection().AddTinyFlags().BuildServiceProvider();
            return provider.GetService<CheckoutConsumer>() is not null;
            """;

        Assert.True(Execute<bool>(body));
    }

    [Fact]
    public void Null_service_collection_is_rejected()
    {
        const string body = """
            try { TinyFlagsServiceCollectionExtensions.AddTinyFlags(null!); return "accepted"; }
            catch (ArgumentNullException error) { return error.ParamName!; }
            """;

        Assert.Equal("services", Execute<string>(body));
    }

    [Fact]
    public void Failed_application_can_be_retried_without_duplicate_services()
    {
        const string body = """
            var fail = true;
            TinyFlagsBootstrap.AddContribution(typeof(Scenario), Array.Empty<FeatureDefinition>(), services =>
            {
                services.TryAddSingleton<CheckoutConsumer>(provider =>
                    new CheckoutConsumer(provider.GetRequiredService<CheckoutFeatureFlags>()));
                if (fail) throw new InvalidOperationException("Registration failed");
            });
            var services = new ServiceCollection();
            try { services.AddTinyFlags(); return false; }
            catch (InvalidOperationException error)
            {
                if (error.Message != "Registration failed") return false;
            }
            fail = false;
            services.AddTinyFlags();
            using var provider = services.BuildServiceProvider();
            return provider.GetServices<CheckoutConsumer>().Count() == 1
                && provider.GetServices<FeatureValues>().Count() == 1;
            """;

        Assert.True(Execute<bool>(body));
    }

    private static T Execute<T>(string body)
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection.Extensions;
            using TinyFlags;
            public class Checkout : IFeatureProvider { public bool Enabled => false; }
            public sealed class CheckoutConsumer
            {
                private readonly CheckoutFeatureFlags _flags;
                public CheckoutConsumer(CheckoutFeatureFlags flags) { _flags = flags; }
                public bool Enabled => _flags.Enabled;
            }
            public static class Scenario
            {
                public static object Run()
                {
            """ + body + """
                }

                private static void RegisterFlags()
                {
                    TinyFlagsBootstrap.AddContribution(typeof(Scenario),
                        Array.Empty<FeatureDefinition>(),
                        services => services.TryAddSingleton<CheckoutConsumer>(provider =>
                            new CheckoutConsumer(provider.GetRequiredService<CheckoutFeatureFlags>())));
                }
            }
            """;
        return SourceGeneratorTestHost.Execute<T>(source);
    }
}
