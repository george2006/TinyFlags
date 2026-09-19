using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shop;
using TinyFlags;

ShopModule.Initialize();
if (args.Contains("--register"))
{
    await RegisterWithServerAsync();
    return;
}

var services = new ServiceCollection().AddTinyFlags();
services.AddTinyFlags();
using var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateOnBuild = true,
    ValidateScopes = true
});
using var scope = provider.CreateScope();
var checkout = provider.GetRequiredService<CheckoutFeatureFlags>();
var home = provider.GetRequiredService<HomeFeatureFlags>();
Require(!checkout.Enabled && checkout.Label == "Buy" && !home.Enabled, "Declared defaults");
Require(ReferenceEquals(checkout, scope.ServiceProvider.GetRequiredService<CheckoutFeatureFlags>()), "Singleton access");
Require(provider.GetServices<CheckoutFeatureFlags>().Count() == 1, "Repeated registration");
var values = provider.GetRequiredService<FeatureValues>();
Require(ReferenceEquals(values, scope.ServiceProvider.GetRequiredService<FeatureValues>()), "Shared store");
values.ReplaceSnapshot(new Dictionary<string, object>
{
    ["Shop.Checkout.Enabled"] = true,
    ["Shop.Checkout.Label"] = "Updated",
    ["Home.Enabled"] = true
});
Require(checkout.Enabled && checkout.Label == "Updated" && home.Enabled, "Existing instances observe updates");
Require(TinyFlagsBootstrap.GetDefinitions().Select(definition => definition.Key).SequenceEqual(
    new[] { "Home.Enabled", "Shop.Checkout.Enabled", "Shop.Checkout.Label" }), "Root catalog composition");
Require(!File.Exists(Path.Combine(AppContext.BaseDirectory, "TinyFlags.SourceGen.dll")), "Generator stays a compiler asset");
Console.WriteLine("TinyFlags package consumer passed.");

static async Task RegisterWithServerAsync()
{
    var builder = Host.CreateApplicationBuilder();
    builder.Services.AddTinyFlags(options =>
    {
        options.Endpoint = new Uri(Environment.GetEnvironmentVariable("TINYFLAGS_TEST_ENDPOINT")!);
        options.ApiKey = Environment.GetEnvironmentVariable("TINYFLAGS_TEST_API_KEY");
        options.RetryDelay = TimeSpan.FromMilliseconds(50);
        options.MaxRetryDelay = TimeSpan.FromMilliseconds(100);
    });
    using var host = builder.Build();
    await host.StartAsync();
    var flags = host.Services.GetRequiredService<CheckoutFeatureFlags>();
    var declaration = new Checkout();
    Require(flags.Enabled == declaration.Enabled && flags.Label == declaration.Label, "Local defaults after startup");
    Require(!host.Services.GetRequiredService<HomeFeatureFlags>().Enabled, "Root assembly default");
    Require(!File.Exists(Path.Combine(AppContext.BaseDirectory, "TinyFlags.SourceGen.dll")), "Generator stays a compiler asset");
    Console.WriteLine("TINYFLAGS_HOST_STARTED");
    Require(await Console.In.ReadLineAsync() == "stop", "Explicit shutdown from integration test");
    await host.StopAsync();
}

static void Require(bool condition, string behavior)
{
    if (!condition)
    {
        throw new InvalidOperationException(behavior);
    }
}

public sealed class Home : IFeatureProvider
{
    public bool Enabled => false;
}
