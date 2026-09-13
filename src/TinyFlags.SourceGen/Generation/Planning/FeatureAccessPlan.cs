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
