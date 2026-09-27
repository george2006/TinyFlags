using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinyFlags.Grpc.TestServer;

namespace TinyFlags.Tests;

public sealed class GrpcRegistrationTests
{
    [Fact]
    public async Task Repeated_configuration_shares_one_transport_and_activates_registration_and_push_only()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var server = await FakeTinyFlagsServer.StartAsync();
        var registrations = 0;
        var watches = 0;
        server.OnRegister = _ =>
        {
            Interlocked.Increment(ref registrations);
            return null;
        };
        async IAsyncEnumerable<(string, long, IReadOnlyList<RawFeatureValue>)> Stream(
            [EnumeratorCancellation] CancellationToken ct)
        {
            Interlocked.Increment(ref watches);
            yield return (Guid.NewGuid().ToString("D"), 1, [RawFeatureValue.Bool("Registration.Probe", true)]);
            await Task.CompletedTask;
        }
        server.OnWatch = Stream;
        var builder = Host.CreateApplicationBuilder();
        for (var i = 0; i < 2; i++)
        {
            builder.Services.AddTinyFlags(options => options.UseGrpcTransport(grpc =>
            {
                grpc.Endpoint = server.Endpoint;
                grpc.ApiKey = "test-key";
            }));
        }
        using var host = builder.Build();

        var transport = Assert.Single(host.Services.GetServices<TinyFlagsGrpcTransport>());
        Assert.Same(transport, Assert.Single(host.Services.GetServices<IFeatureDefinitionsTransport>()));
        Assert.Same(transport, Assert.Single(host.Services.GetServices<IFeatureValuesSubscription>()));
        Assert.Null(host.Services.GetService<IFeatureValuesTransport>());
        Assert.Null(host.Services.GetService<FeatureValuesPollingOptions>());
        var workers = host.Services.GetServices<IHostedService>().ToArray();
        Assert.Equal(2, workers.Length);
        var registration = Assert.Single(workers.OfType<TinyFlagsRegistrationWorker>());
        var watch = Assert.Single(workers.OfType<TinyFlagsValuesWatchWorker>());
        Assert.Equal(0, registrations);
        Assert.Equal(0, watches);

        await host.StartAsync(timeout.Token);
        try
        {
            await registration.ExecuteTask!.WaitAsync(timeout.Token);
            await watch.ExecuteTask!.WaitAsync(timeout.Token);

            Assert.Equal(1, registrations);
            Assert.Equal(1, watches);
            Assert.True(host.Services.GetRequiredService<FeatureValues>().GetBoolean("Registration.Probe", false));
        }
        finally
        {
            await host.StopAsync(timeout.Token);
        }
    }

    [Theory]
    [InlineData("endpoint")]
    [InlineData("key")]
    [InlineData("reconnect")]
    public void Conflicting_configuration_preserves_the_original_settings_and_registrations(string difference)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTinyFlags(options => options.UseGrpcTransport(grpc =>
        {
            grpc.Endpoint = new Uri("https://flags.example.com");
            grpc.ApiKey = "original-key";
        }));
        var registrationCount = builder.Services.Count;

        var error = Assert.Throws<InvalidOperationException>(() => builder.Services.AddTinyFlags(options => options.UseGrpcTransport(grpc =>
        {
            grpc.Endpoint = new Uri(difference == "endpoint" ? "https://other.example.com" : "https://flags.example.com");
            grpc.ApiKey = difference == "key" ? "other-key" : "original-key";
            grpc.ReconnectDelay = TimeSpan.FromSeconds(difference == "reconnect" ? 2 : 1);
        })));

        Assert.DoesNotContain("original-key", error.Message);
        Assert.DoesNotContain("other-key", error.Message);
        Assert.Equal(registrationCount, builder.Services.Count);
        using var host = builder.Build();
        var settings = host.Services.GetRequiredService<TinyFlagsGrpcOptions>();
        Assert.Equal(new Uri("https://flags.example.com"), settings.Endpoint);
        Assert.Equal("original-key", settings.ApiKey);
        Assert.Equal(TimeSpan.FromSeconds(1), settings.ReconnectDelay);
        Assert.Equal(2, host.Services.GetServices<IHostedService>().Count());
    }
}
