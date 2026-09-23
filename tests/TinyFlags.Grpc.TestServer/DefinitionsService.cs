using Grpc.Core;
using CoreFeatureDefinition = TinyFlags.FeatureDefinition;
using ProtoFeatureDefinition = TinyFlags.Grpc.FeatureDefinition;

namespace TinyFlags.Grpc.TestServer;

internal sealed class DefinitionsService(FakeTinyFlagsServer server) : TinyFlagsDefinitions.TinyFlagsDefinitionsBase
{
    public override Task<RegisterResponse> Register(RegisterRequest request, ServerCallContext context)
    {
        var definitions = request.Definitions.Select(ToFeatureDefinition).ToArray();
        if (server.OnRegister?.Invoke(definitions) is { } status)
        {
            throw new RpcException(new Status(status, "test"));
        }
        return Task.FromResult(new RegisterResponse());
    }

    private static CoreFeatureDefinition ToFeatureDefinition(ProtoFeatureDefinition definition) => definition.DefaultValueCase switch
    {
        ProtoFeatureDefinition.DefaultValueOneofCase.BoolDefault => CoreFeatureDefinition.Boolean(definition.Key, definition.BoolDefault),
        ProtoFeatureDefinition.DefaultValueOneofCase.StringDefault => CoreFeatureDefinition.String(definition.Key, definition.StringDefault),
        _ => throw new InvalidOperationException("Test sent a definition with no default value.")
    };
}
