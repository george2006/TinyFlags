using System.Net;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using CoreFeatureDefinition = TinyFlags.FeatureDefinition;

namespace TinyFlags.Grpc.TestServer;

/// <summary>
/// A real gRPC server (Kestrel, HTTP/2 loopback) implementing both tinyflags.proto services, with
/// per-test-controllable behavior - not a mock of TinyFlagsGrpcTransport's dependencies, a real
/// collaborator on the other end of a real channel, matching how TinyFlags.Http.Tests exercises
/// TinyFlagsApiClient against a real loopback Kestrel server.
/// </summary>
public sealed class FakeTinyFlagsServer : IAsyncDisposable
{
    private WebApplication? app;

    public Uri Endpoint { get; private set; } = null!;

    /// <summary>
    /// Called once per Register call with the definitions the client sent, translated to
    /// <see cref="FeatureDefinition"/>. Return null for success, or a status code to fail the call.
    /// </summary>
    public Func<IReadOnlyList<CoreFeatureDefinition>, StatusCode?>? OnRegister { get; set; }

    /// <summary>
    /// Called once per Watch call (so once per connection attempt, including reconnects). Yield a
    /// snapshot to send it; let the enumerable complete to end the stream cleanly; throw
    /// <see cref="FakeGrpcStatusException"/> to end it with a specific status instead.
    /// EnvironmentId is a raw string and Values a plain list of <see cref="RawFeatureValue"/>, not
    /// a Guid/Dictionary - deliberately permissive, so a test can put a malformed environment id,
    /// duplicate keys, or a kind/value mismatch on the wire the same way a real buggy server
    /// could, rather than being structurally prevented from it.
    /// </summary>
    public Func<CancellationToken, IAsyncEnumerable<(string EnvironmentId, long Revision, IReadOnlyList<RawFeatureValue> Values)>>? OnWatch { get; set; }

    private FakeTinyFlagsServer()
    {
    }

    public static async Task<FakeTinyFlagsServer> StartAsync()
    {
        var server = new FakeTinyFlagsServer();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(server);

        server.app = builder.Build();
        server.app.MapGrpcService<DefinitionsService>();
        server.app.MapGrpcService<ValuesService>();
        await server.app.StartAsync().ConfigureAwait(false);
        server.Endpoint = new Uri(server.app.Urls.Single());
        return server;
    }

    public ValueTask DisposeAsync() => app?.DisposeAsync() ?? ValueTask.CompletedTask;
}
