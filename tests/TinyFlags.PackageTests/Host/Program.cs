using Microsoft.Extensions.DependencyInjection;
using Shop;
using TinyFlags;

ShopModule.Initialize();
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
