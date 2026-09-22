using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

public static class TinyFlagsServiceCollectionExtensions
{
    /// <summary>
    /// Registers a shared local store and applies contributions from initialized assemblies.
    /// </summary>
    public static IServiceCollection AddTinyFlags(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<FeatureValues>();
        TinyFlagsBootstrap.Apply(services);
        return services;
    }

    /// <summary>
    /// Registers local flag access, background registration and initial value synchronization after host startup.
    /// </summary>
    public static IServiceCollection AddTinyFlags(this IServiceCollection services, Action<TinyFlagsClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new TinyFlagsClientOptions();
        configure(options);
        var snapshot = options.CreateSnapshot();
        var existing = services.LastOrDefault(service => service.ServiceType == typeof(TinyFlagsClientOptions));
        if (existing is not null
            && (existing.ImplementationInstance is not TinyFlagsClientOptions registered || !registered.HasSameConfigurationAs(snapshot)))
        {
            throw new InvalidOperationException("TinyFlags is already configured with different client settings.");
        }

        services.AddTinyFlags();
        if (existing is null)
        {
            services.AddSingleton(snapshot);
            services.AddSingleton(new FeatureSnapshotReader(snapshot.MaxSnapshotBytes));
            services.AddSingleton(provider => new TinyFlagsApiClient(snapshot,
                provider.GetRequiredService<FeatureSnapshotReader>(), provider.GetRequiredService<ILogger<TinyFlagsApiClient>>()));
            services.TryAddSingleton<IFeatureDefinitionsTransport>(provider => provider.GetRequiredService<TinyFlagsApiClient>());
        }
        services.AddHostedService<TinyFlagsRegistrationWorker>();
        services.AddHostedService<TinyFlagsSynchronizationWorker>();
        return services;
    }
}
