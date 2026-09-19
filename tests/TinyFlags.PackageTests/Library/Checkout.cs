using TinyFlags;

namespace Shop;

public sealed class Checkout : IFeatureProvider
{
#if UPDATED_DEFAULTS
    public bool Enabled => true;
    public string Label => "Buy v2";
    public bool AddedLater => true;
#else
    public bool Enabled => false;
    public string Label => "Buy";
#endif
}

public static class ShopModule
{
    public static void Initialize() { }
}
