using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrdersService;
using Shared;
using TinyFlags;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTinyFlags(options =>
{
    options.Endpoint = new Uri(builder.Configuration["TinyFlags:Endpoint"]
        ?? throw new InvalidOperationException("TinyFlags:Endpoint is required."));
    options.ApiKey = builder.Configuration["TinyFlags:ApiKey"]
        ?? throw new InvalidOperationException("TinyFlags:ApiKey is required.");
    // Shorter than the 30s default so the sample feels responsive when you flip a value.
    options.RefreshInterval = TimeSpan.FromSeconds(5);
});

var host = builder.Build();
await host.StartAsync();

var checkout = host.Services.GetRequiredService<CheckoutFeatureFlags>();
var promotions = host.Services.GetRequiredService<PromotionsFeatureFlags>();

Console.WriteLine("OrdersService started. Polling TinyFlags every 5s — Ctrl+C to stop.");
while (true)
{
    Console.WriteLine($"[{DateTimeOffset.Now:T}] ExpressCheckout={checkout.ExpressCheckout}  " +
        $"Shared.Promotions.HolidaySaleBanner={promotions.HolidaySaleBanner}");
    await Task.Delay(TimeSpan.FromSeconds(5));
}
