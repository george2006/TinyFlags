using TinyFlags;

namespace OrdersService;

// Private to this service: PaymentsService declares nothing with this namespace/class/property,
// so nothing else shares this key.
public sealed class Checkout : IFeatureProvider
{
    public bool ExpressCheckout => false;
}
