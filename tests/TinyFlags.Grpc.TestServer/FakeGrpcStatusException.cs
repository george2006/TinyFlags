using Grpc.Core;

namespace TinyFlags.Grpc.TestServer;

/// <summary>
/// Thrown from a test's <see cref="FakeTinyFlagsServer.OnWatch"/> stream to end the call with a
/// specific gRPC status instead of completing cleanly - <see cref="ValuesService"/> converts it to
/// a real <see cref="RpcException"/>, so <c>TinyFlagsGrpcTransport</c> sees exactly what it would
/// against a real server.
/// </summary>
public sealed class FakeGrpcStatusException(StatusCode statusCode) : Exception
{
    public StatusCode StatusCode { get; } = statusCode;
}
