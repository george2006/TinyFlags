namespace TinyFlags.Tests;

public sealed class FeatureDefinitionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Boolean_definition_preserves_the_key_and_typed_default(bool defaultValue)
    {
        var definition = FeatureDefinition.Boolean("Shop.Checkout.Enabled", defaultValue);

        Assert.Equal("Shop.Checkout.Enabled", definition.Key);
        Assert.Equal(FeatureKind.Boolean, definition.Kind);
        Assert.Equal(defaultValue, Assert.IsType<bool>(definition.DefaultValue));
    }

    [Theory]
    [InlineData("")]
    [InlineData("true")]
    [InlineData(" Comprar \n")]
    [InlineData("\u00e1\u2028\ud83d\ude00")]
    public void String_definition_preserves_the_key_and_exact_text(string defaultValue)
    {
        var definition = FeatureDefinition.String("Shop.Checkout.Label", defaultValue);

        Assert.Equal("Shop.Checkout.Label", definition.Key);
        Assert.Equal(FeatureKind.String, definition.Kind);
        Assert.Equal(defaultValue, Assert.IsType<string>(definition.DefaultValue));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void Definitions_reject_missing_or_blank_keys(string? key)
    {
        var booleanError = Assert.ThrowsAny<ArgumentException>(() => FeatureDefinition.Boolean(key!, false));
        var stringError = Assert.ThrowsAny<ArgumentException>(() => FeatureDefinition.String(key!, "Buy"));

        Assert.Equal("key", booleanError.ParamName);
        Assert.Equal("key", stringError.ParamName);
    }

    [Fact]
    public void String_definition_rejects_a_null_default()
    {
        var error = Assert.Throws<ArgumentNullException>(() =>
            FeatureDefinition.String("Shop.Checkout.Label", null!));

        Assert.Equal("defaultValue", error.ParamName);
    }
}
