using TinyFlags;

namespace Shared;

// Deliberately duplicated in OrdersService/SharedPromotions.cs, not shared via a project or
// package reference. Flag identity is namespace + class + property name, computed independently
// by each compilation — see docs/multi-service-flags.md. Keep both copies identical.
public sealed class Promotions : IFeatureProvider
{
    public bool HolidaySaleBanner => false;
}
