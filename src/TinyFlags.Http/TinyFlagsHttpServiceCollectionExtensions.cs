using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

public static class TinyFlagsHttpServiceCollectionExtensions
{
    /// <summary>
    /// Registers the reference HTTP transport: local flag access, background registration and
    /// polling value synchronization after host startup.
    /// </summary>
    public static IServiceCollection UseHttpTransport(this IServiceCollection services, Action<TinyFlagsHttpOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new TinyFlagsHttpOptions();
        configure(options);
        var snapshot = options.CreateSnapshot();
        var existing = services.LastOrDefault(service => service.ServiceType == typeof(TinyFlagsHttpOptions));
        if (existing is not null
            && (existing.ImplementationInstance is not TinyFlagsHttpOptions registered || !registered.HasSameConfigurationAs(snapshot)))
        {
            throw new InvalidOperationException("TinyFlags HTTP transport is already configured with different settings.");
        }

        services.AddTinyFlags();
        if (existing is null)
        {
            services.AddSingleton(snapshot);
            services.AddSingleton(new FeatureValuesPollingOptions { RefreshInterval = snapshot.RefreshInterval });
            services.AddSingleton(new FeatureSnapshotReader(snapshot.MaxSnapshotBytes));
            services.AddSingleton(provider => new TinyFlagsApiClient(snapshot,
                provider.GetRequiredService<FeatureSnapshotReader>(), provider.GetRequiredService<ILogger<TinyFlagsApiClient>>()));
            services.TryAddSingleton<IFeatureDefinitionsTransport>(provider => provider.GetRequiredService<TinyFlagsApiClient>());
            services.TryAddSingleton<IFeatureValuesTransport>(provider => provider.GetRequiredService<TinyFlagsApiClient>());
        }
        services.AddHostedService<TinyFlagsRegistrationWorker>();
        services.AddHostedService<TinyFlagsSynchronizationWorker>();
        return services;
    }
}
