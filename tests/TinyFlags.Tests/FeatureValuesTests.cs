namespace TinyFlags.Tests;

public sealed class FeatureValuesTests
{
    [Fact]
    public void Uses_each_callers_default_when_the_flag_is_missing()
    {
        var values = new FeatureValues();

        Assert.True(values.GetBoolean("Checkout.Enabled", true));
        Assert.False(values.GetBoolean("Checkout.Enabled", false));
        Assert.Equal("Comprar", values.GetString("Checkout.Label", "Comprar"));
        Assert.Equal("Buy", values.GetString("Checkout.Label", "Buy"));
    }

    [Fact]
    public void Reads_false_and_empty_string_as_configured_values()
    {
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object>
        {
            ["Checkout.Enabled"] = false,
            ["Checkout.Label"] = string.Empty
        });

        Assert.False(values.GetBoolean("Checkout.Enabled", true));
        Assert.Equal(string.Empty, values.GetString("Checkout.Label", "Comprar"));
    }

    [Fact]
    public void Existing_store_reads_new_values_after_replacement()
    {
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object>
        {
            ["Checkout.Enabled"] = false,
            ["Checkout.Label"] = "Comprar"
        });

        Assert.False(values.GetBoolean("Checkout.Enabled", true));
        Assert.Equal("Comprar", values.GetString("Checkout.Label", "default"));

        values.ReplaceSnapshot(new Dictionary<string, object>
        {
            ["Checkout.Enabled"] = true,
            ["Checkout.Label"] = "Buy"
        });

        Assert.True(values.GetBoolean("Checkout.Enabled", false));
        Assert.Equal("Buy", values.GetString("Checkout.Label", "default"));
    }

    [Fact]
    public void Replacement_removes_flags_absent_from_the_new_snapshot()
    {
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object> { ["Checkout.Enabled"] = true });
        values.ReplaceSnapshot(new Dictionary<string, object> { ["Checkout.Label"] = "Buy" });

        Assert.False(values.GetBoolean("Checkout.Enabled", false));
        Assert.Equal("Buy", values.GetString("Checkout.Label", "default"));

        values.ReplaceSnapshot(new Dictionary<string, object>());

        Assert.Equal("default", values.GetString("Checkout.Label", "default"));
    }

    [Fact]
    public void Uses_defaults_when_a_flags_type_does_not_match_the_reader()
    {
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object>
        {
            ["Checkout.Enabled"] = "true",
            ["Checkout.Label"] = true
        });

        Assert.False(values.GetBoolean("Checkout.Enabled", false));
        Assert.Equal("Comprar", values.GetString("Checkout.Label", "Comprar"));
    }

    [Fact]
    public void Caller_mutations_cannot_change_a_published_snapshot()
    {
        var values = new FeatureValues();
        var suppliedValues = new Dictionary<string, object> { ["Checkout.Enabled"] = true };
        values.ReplaceSnapshot(suppliedValues);

        suppliedValues["Checkout.Enabled"] = false;
        suppliedValues["Checkout.Label"] = "Buy";

        Assert.True(values.GetBoolean("Checkout.Enabled", false));
        Assert.Equal("default", values.GetString("Checkout.Label", "default"));

        suppliedValues.Clear();

        Assert.True(values.GetBoolean("Checkout.Enabled", false));
    }

    [Fact]
    public void Flag_keys_remain_case_sensitive_regardless_of_the_supplied_comparer()
    {
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Checkout.Enabled"] = true
        });

        Assert.True(values.GetBoolean("Checkout.Enabled", false));
        Assert.False(values.GetBoolean("checkout.enabled", false));
    }

    [Theory]
    [InlineData(42)]
    [InlineData(null)]
    public void Invalid_updates_preserve_the_entire_previous_snapshot(object? invalidValue)
    {
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object> { ["Checkout.Enabled"] = true });

        Assert.Throws<ArgumentException>(() => values.ReplaceSnapshot(new Dictionary<string, object>
        {
            ["Checkout.Enabled"] = false,
            ["Checkout.Label"] = invalidValue!
        }));

        Assert.True(values.GetBoolean("Checkout.Enabled", false));
        Assert.Equal("default", values.GetString("Checkout.Label", "default"));
    }

    [Fact]
    public void Concurrent_readers_do_not_observe_partially_replaced_snapshots()
    {
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object> { ["Checkout.Label"] = "Before" });

        Parallel.For(0, 2_000, index =>
        {
            if (index % 2 == 0)
            {
                values.ReplaceSnapshot(new Dictionary<string, object>
                {
                    ["Checkout.Label"] = index % 4 == 0 ? "Before" : "After"
                });
            }
            else
            {
                Assert.Contains(values.GetString("Checkout.Label", "Missing"), new[] { "Before", "After" });
            }
        });
    }
}
