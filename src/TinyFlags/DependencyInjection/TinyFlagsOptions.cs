using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyFlags;

/// <summary>
/// Passed to <see cref="TinyFlagsServiceCollectionExtensions.AddTinyFlags"/>'s configure callback.
/// Transport packages register their dependencies through <see cref="Services"/> and declare
/// capabilities through the Use methods. Core registers the corresponding hosted workers.
/// </summary>
public sealed class TinyFlagsOptions
{
    /// <summary>Registers concrete transport singletons and their dependencies.</summary>
    public IServiceCollection Services { get; }

    internal TinyFlagsOptions(IServiceCollection services) => Services = services;

    /// <summary>
    /// Uses an existing singleton to register definitions after host startup.
    /// An explicitly registered definitions transport is preserved.
    /// </summary>
    public TinyFlagsOptions UseDefinitionsTransport<TTransport>() where TTransport : class, IFeatureDefinitionsTransport
    {
        Services.TryAddSingleton<IFeatureDefinitionsTransport>(provider => provider.GetRequiredService<TTransport>());
        Services.AddHostedService<TinyFlagsRegistrationWorker>();
        return this;
    }

    /// <summary>
    /// Uses an existing singleton to poll values after host startup.
    /// Register <see cref="FeatureValuesPollingOptions"/> to supply the polling interval.
    /// An explicitly registered pull transport is preserved.
    /// </summary>
    public TinyFlagsOptions UsePullTransport<TTransport>() where TTransport : class, IFeatureValuesTransport
    {
        Services.TryAddSingleton<IFeatureValuesTransport>(provider => provider.GetRequiredService<TTransport>());
        Services.AddHostedService<TinyFlagsSynchronizationWorker>();
        return this;
    }

    /// <summary>
    /// Uses an existing singleton to watch values after host startup.
    /// An explicitly registered push transport is preserved.
    /// </summary>
    public TinyFlagsOptions UsePushTransport<TTransport>() where TTransport : class, IFeatureValuesSubscription
    {
        Services.TryAddSingleton<IFeatureValuesSubscription>(provider => provider.GetRequiredService<TTransport>());
        Services.AddHostedService<TinyFlagsValuesWatchWorker>();
        return this;
    }
}
