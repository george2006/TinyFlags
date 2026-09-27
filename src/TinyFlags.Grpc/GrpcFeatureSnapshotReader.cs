using System;
using System.Collections.Generic;
using System.Linq;
using TinyFlags.Grpc;
using ProtoFeatureKind = TinyFlags.Grpc.FeatureKind;

namespace TinyFlags;

// Valid unknown keys are allowed; known keys must match their declared kind.
internal sealed class GrpcFeatureSnapshotReader
{
    public FeatureValuesResult Read(ValuesSnapshot snapshot, IReadOnlyList<FeatureDefinition> catalog)
    {
        if (!Guid.TryParse(snapshot.EnvironmentId, out var environmentId) || environmentId == Guid.Empty)
        {
            throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
        }
        if (snapshot.Revision < 0)
        {
            throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
        }

        var knownKinds = catalog.ToDictionary(definition => definition.Key, definition => definition.Kind, StringComparer.Ordinal);
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var value in snapshot.Values)
        {
            if (string.IsNullOrWhiteSpace(value.Key))
            {
                throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
            }

            var (kind, typedValue) = ToKindAndValue(value);
            if (knownKinds.TryGetValue(value.Key, out var knownKind) && knownKind != kind)
            {
                throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
            }
            if (!values.TryAdd(value.Key, typedValue))
            {
                throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
            }
        }
        return FeatureValuesResult.Updated(new FeatureValuesCursor(environmentId, snapshot.Revision), values);
    }

    // The kind and oneof must agree; an unset oneof otherwise exposes a default value.
    private static (FeatureKind Kind, object Value) ToKindAndValue(FeatureValue value) => (value.Kind, value.ValueCase) switch
    {
        (ProtoFeatureKind.Boolean, FeatureValue.ValueOneofCase.BoolValue) => (FeatureKind.Boolean, value.BoolValue),
        (ProtoFeatureKind.String, FeatureValue.ValueOneofCase.StringValue) => (FeatureKind.String, value.StringValue),
        _ => throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse)
    };
}
