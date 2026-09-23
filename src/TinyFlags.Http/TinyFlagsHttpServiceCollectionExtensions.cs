using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

public static class TinyFlagsHttpServiceCollectionExtensions
{
    /// <summary>
    /// Picks the reference HTTP transport: local flag access, background registration and polling
    /// value synchronization after host startup. Called from <c>AddTinyFlags</c>'s configure
    /// callback - <c>services.AddTinyFlags(tinyFlags => tinyFlags.UseHttpTransport(...))</c>.
    /// </summary>
    public static TinyFlagsOptions UseHttpTransport(this TinyFlagsOptions tinyFlags, Action<TinyFlagsHttpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(tinyFlags);
        ArgumentNullException.ThrowIfNull(configure);
        var services = tinyFlags.Services;
        var options = new TinyFlagsHttpOptions();
        configure(options);
        var snapshot = options.CreateSnapshot();

        if (services.RegisterSingletonOnce(snapshot, static (a, b) => a.HasSameConfigurationAs(b),
                "TinyFlags HTTP transport is already configured with different settings."))
        {
            services.AddSingleton(new FeatureValuesPollingOptions { RefreshInterval = snapshot.RefreshInterval });
            services.AddSingleton(new FeatureSnapshotReader(snapshot.MaxSnapshotBytes));
            services.AddSingleton(provider => new TinyFlagsApiClient(snapshot,
                provider.GetRequiredService<FeatureSnapshotReader>(), provider.GetRequiredService<ILogger<TinyFlagsApiClient>>()));
            services.TryAddSingleton<IFeatureDefinitionsTransport>(provider => provider.GetRequiredService<TinyFlagsApiClient>());
            services.TryAddSingleton<IFeatureValuesTransport>(provider => provider.GetRequiredService<TinyFlagsApiClient>());
        }
        services.AddHostedService<TinyFlagsRegistrationWorker>();
        services.AddHostedService<TinyFlagsSynchronizationWorker>();
        return tinyFlags;
    }
}
