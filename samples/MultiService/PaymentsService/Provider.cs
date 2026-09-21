using TinyFlags;

namespace PaymentsService;

// Private to this service: OrdersService declares nothing with this namespace/class/property,
// so nothing else shares this key. Also demonstrates a String flag, not just Boolean.
public sealed class Provider : IFeatureProvider
{
    public string PreferredGateway => "Stripe";
}
