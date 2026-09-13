using System;
using System.Collections.Generic;
using System.Linq;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Generation.Planning;

internal sealed class FeatureAccessPlanner
{
    public FeatureAccessPlan Create(FeatureProviderDefinition provider)
    {
        var names = new HashSet<string>(provider.Features.Select(feature => feature.Name), StringComparer.Ordinal);
        var fieldName = "_values";

        while (names.Contains(fieldName))
        {
            fieldName += "_";
        }

        return new FeatureAccessPlan(provider, fieldName);
    }
}
