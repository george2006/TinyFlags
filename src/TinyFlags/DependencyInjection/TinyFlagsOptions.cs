using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyFlags;

/// <summary>
/// Passed to <see cref="TinyFlagsServiceCollectionExtensions.AddTinyFlags"/>'s configure callback.
/// A transport package (e.g. TinyFlags.Http) contributes its own extension method on this type -
/// <c>tinyFlags.UseHttpTransport(...)</c> - registering directly against <see cref="Services"/>,
/// so core never needs to know a given transport exists.
/// </summary>
public sealed class TinyFlagsOptions
{
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
