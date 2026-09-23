using Grpc.Core;
using ProtoFeatureKind = TinyFlags.Grpc.FeatureKind;
using ProtoFeatureValue = TinyFlags.Grpc.FeatureValue;

namespace TinyFlags.Grpc.TestServer;

internal sealed class ValuesService(FakeTinyFlagsServer server) : TinyFlagsValues.TinyFlagsValuesBase
{
    public override async Task Watch(WatchRequest request, IServerStreamWriter<ValuesSnapshot> responseStream, ServerCallContext context)
    {
        if (server.OnWatch is null)
        {
            return;
        }

        try
        {
            await foreach (var (environmentId, revision, values) in server.OnWatch(context.CancellationToken).ConfigureAwait(false))
            {
                var snapshot = new ValuesSnapshot { EnvironmentId = environmentId, Revision = revision };
                snapshot.Values.AddRange(values.Select(ToProtoValue));
                await responseStream.WriteAsync(snapshot, context.CancellationToken).ConfigureAwait(false);
            }
        }
        catch (FakeGrpcStatusException status)
        {
            throw new RpcException(new Status(status.StatusCode, "test"));
        }
    }

    private static ProtoFeatureValue ToProtoValue(RawFeatureValue entry)
    {
        var value = new ProtoFeatureValue
        {
            Key = entry.Key,
            Kind = entry.Kind switch
            {
                RawFeatureKind.Boolean => ProtoFeatureKind.Boolean,
                RawFeatureKind.String => ProtoFeatureKind.String,
                _ => ProtoFeatureKind.Unspecified
            }
        };
        // Independent of Kind above, on purpose: lets a test set Kind without the matching value
        // (or neither value), to exercise TinyFlagsGrpcTransport's own cross-check.
        if (entry.BoolValue is { } boolValue)
        {
            value.BoolValue = boolValue;
        }
        else if (entry.StringValue is { } stringValue)
        {
            value.StringValue = stringValue;
        }
        return value;
    }
}
