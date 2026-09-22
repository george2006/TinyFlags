using OrdersService;
using Shared;
using TinyFlags;

var builder = WebApplication.CreateBuilder(args);
builder.Services.UseHttpTransport(options =>
{
    options.Endpoint = new Uri(builder.Configuration["TinyFlags:Endpoint"]
        ?? throw new InvalidOperationException("TinyFlags:Endpoint is required."));
    options.ApiKey = builder.Configuration["TinyFlags:ApiKey"]
        ?? throw new InvalidOperationException("TinyFlags:ApiKey is required.");
    // Shorter than the 30s default so the sample feels responsive when you flip a value.
    options.RefreshInterval = TimeSpan.FromSeconds(5);
});

var app = builder.Build();

app.MapGet("/flags", (CheckoutFeatureFlags checkout, PromotionsFeatureFlags promotions) => new
{
    expressCheckout = checkout.ExpressCheckout,
    sharedHolidaySaleBanner = promotions.HolidaySaleBanner
});

app.Run();
