using TinyFlags;

namespace MyApp;

// Same declaration as samples/SingleServiceHttp - the flag doesn't change per transport, only how
// values arrive does. See docs/getting-started.md's "Option C: connect to a server, real-time".
public sealed class Checkout : IFeatureProvider
{
    public bool NuevoCheckout => false;
    public string TextoBoton => "Comprar";
}
