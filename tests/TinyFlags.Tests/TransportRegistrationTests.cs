using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TinyFlags.Tests;

public sealed class TransportRegistrationTests
{
    [Theory]
    [InlineData("definitions", typeof(TinyFlagsRegistrationWorker))]
    [InlineData("pull", typeof(TinyFlagsSynchronizationWorker))]
    [InlineData("push", typeof(TinyFlagsValuesWatchWorker))]
    public void Each_capability_registers_only_its_contract_and_worker(string capability, Type workerType)
    {
        var builder = Host.CreateApplicationBuilder();
        var transport = new TestTransport();
        builder.Services.AddSingleton(transport);
        builder.Services.AddSingleton(new FeatureValuesPollingOptions());
        builder.Services.AddTinyFlags(options =>
        {
            switch (capability)
            {
                case "definitions": options.UseDefinitionsTransport<TestTransport>(); break;
                case "pull": options.UsePullTransport<TestTransport>(); break;
                case "push": options.UsePushTransport<TestTransport>(); break;
            }
        });
        using var host = builder.Build();

        Assert.IsType(workerType, Assert.Single(host.Services.GetServices<IHostedService>()));
        Assert.Same(capability == "definitions" ? transport : null, host.Services.GetService<IFeatureDefinitionsTransport>());
        Assert.Same(capability == "pull" ? transport : null, host.Services.GetService<IFeatureValuesTransport>());
        Assert.Same(capability == "push" ? transport : null, host.Services.GetService<IFeatureValuesSubscription>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_registration_shares_one_transport_and_activates_only_the_selected_workers(bool push)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var builder = Host.CreateApplicationBuilder();
        var transport = new TestTransport();
        builder.Services.AddSingleton(transport);
        if (!push)
        {
            builder.Services.AddSingleton(new FeatureValuesPollingOptions { RefreshInterval = TimeSpan.FromDays(1) });
        }
        for (var i = 0; i < 2; i++)
        {
            builder.Services.AddTinyFlags(options =>
            {
                options.UseDefinitionsTransport<TestTransport>();
                if (push)
                {
                    options.UsePushTransport<TestTransport>();
                }
                else
                {
                    options.UsePullTransport<TestTransport>();
                }
            });
        }
        using var host = builder.Build();
        var workers = host.Services.GetServices<IHostedService>().ToArray();

        Assert.Equal(2, workers.Length);
        var registration = Assert.Single(workers.OfType<TinyFlagsRegistrationWorker>());
        Assert.Same(transport, host.Services.GetRequiredService<IFeatureDefinitionsTransport>());
        if (push)
        {
            Assert.Same(transport, host.Services.GetRequiredService<IFeatureValuesSubscription>());
            Assert.Single(workers.OfType<TinyFlagsValuesWatchWorker>());
            Assert.Null(host.Services.GetService<IFeatureValuesTransport>());
        }
        else
        {
            Assert.Same(transport, host.Services.GetRequiredService<IFeatureValuesTransport>());
            Assert.Single(workers.OfType<TinyFlagsSynchronizationWorker>());
            Assert.Null(host.Services.GetService<IFeatureValuesSubscription>());
        }
        Assert.Equal(0, transport.RegistrationCalls);
        Assert.False(transport.ValuesRequested.Task.IsCompleted);

        await host.StartAsync(timeout.Token);
        try
        {
            await registration.ExecuteTask!.WaitAsync(timeout.Token);
            await transport.ValuesRequested.Task.WaitAsync(timeout.Token);
            Assert.Equal(1, transport.RegistrationCalls);
            Assert.True(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested);
        }
        finally
        {
            await host.StopAsync(timeout.Token);
        }
    }

    [Fact]
    public void Explicitly_registered_contracts_are_preserved()
    {
        var services = new ServiceCollection();
        var existing = new TestTransport();
        services.AddSingleton<IFeatureDefinitionsTransport>(existing);
        services.AddSingleton<IFeatureValuesTransport>(existing);
        services.AddSingleton<IFeatureValuesSubscription>(existing);
        services.AddSingleton(new TestTransport());
        services.AddTinyFlags(options => options
            .UseDefinitionsTransport<TestTransport>()
            .UsePullTransport<TestTransport>()
            .UsePushTransport<TestTransport>());
        using var provider = services.BuildServiceProvider();

        Assert.Same(existing, provider.GetRequiredService<IFeatureDefinitionsTransport>());
        Assert.Same(existing, provider.GetRequiredService<IFeatureValuesTransport>());
        Assert.Same(existing, provider.GetRequiredService<IFeatureValuesSubscription>());
    }

    private sealed class TestTransport : IFeatureDefinitionsTransport, IFeatureValuesTransport, IFeatureValuesSubscription
    {
        public int RegistrationCalls { get; private set; }
        public TaskCompletionSource ValuesRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RegisterAsync(IReadOnlyList<FeatureDefinition> definitions, CancellationToken ct = default)
        {
            RegistrationCalls++;
            return Task.CompletedTask;
        }

        public Task<FeatureValuesResult> GetValuesAsync(IReadOnlyList<FeatureDefinition> catalog,
            FeatureValuesCursor? current, CancellationToken ct = default)
        {
            ValuesRequested.TrySetResult();
            return Task.FromResult(FeatureValuesResult.Unchanged());
        }

        public async IAsyncEnumerable<FeatureValuesResult> WatchAsync(IReadOnlyList<FeatureDefinition> catalog,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            ValuesRequested.TrySetResult();
            await Task.CompletedTask;
            yield break;
        }
    }
}
