using MyApp;
using TinyFlags;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTinyFlags(tinyFlags => tinyFlags.UseGrpcTransport(options =>
{
    options.Endpoint = new Uri(builder.Configuration["TinyFlags:Endpoint"]
        ?? throw new InvalidOperationException("TinyFlags:Endpoint is required."));
    options.ApiKey = builder.Configuration["TinyFlags:ApiKey"]
        ?? throw new InvalidOperationException("TinyFlags:ApiKey is required.");
}));

var app = builder.Build();

app.MapGet("/flags", (CheckoutFeatureFlags checkout) => new
{
    nuevoCheckout = checkout.NuevoCheckout,
    textoBoton = checkout.TextoBoton
});

app.Run();
