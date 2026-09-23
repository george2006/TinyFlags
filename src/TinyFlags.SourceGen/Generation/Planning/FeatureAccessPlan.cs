using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Generation.Planning;

internal sealed class FeatureAccessPlan
{
    public FeatureAccessPlan(FeatureProviderDefinition provider, string fieldName)
    {
        Provider = provider;
        FieldName = fieldName;
    }

    public FeatureProviderDefinition Provider { get; }

    public string ClassName => Provider.Name + "FeatureFlags";

    // FeatureAccessEmitter emits property names with a verbatim "@" prefix so a flag named after a
    // C# keyword still compiles (see WriteProperty) - stripped back out here since a source-output
    // hint name isn't a C# identifier and doesn't need to survive that escaping.
    public string HintName => Provider.QualifiedName.Replace("@", string.Empty) + "FeatureFlags.g.cs";

    public string FieldName { get; }

    public override bool Equals(object? obj)
    {
        return obj is FeatureAccessPlan other
            && Provider.Equals(other.Provider)
            && FieldName == other.FieldName;
    }

    public override int GetHashCode()
    {
        return (Provider, FieldName).GetHashCode();
    }
}
