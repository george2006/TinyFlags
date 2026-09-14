using TinyFlags;

namespace Shop;

public sealed class Checkout : IFeatureProvider
{
    public bool Enabled => false;
    public string Label => "Buy";
}

public static class ShopModule
{
    public static void Initialize() { }
}
