using TinyFlags;

namespace MyApp;

// The exact declaration from docs/getting-started.md's "Option B: Connect to a server" - this
// sample is that doc's example, actually running.
public sealed class Checkout : IFeatureProvider
{
    public bool NuevoCheckout => false;
    public string TextoBoton => "Comprar";
}
