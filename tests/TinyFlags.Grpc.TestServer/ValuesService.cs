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

    private static ProtoFeatureValue ToProtoValue((string Key, object Value) entry)
    {
        var value = new ProtoFeatureValue { Key = entry.Key, Kind = entry.Value is bool ? ProtoFeatureKind.Boolean : ProtoFeatureKind.String };
        if (entry.Value is bool boolValue)
        {
            value.BoolValue = boolValue;
        }
        else
        {
            value.StringValue = (string)entry.Value;
        }
        return value;
    }
}
