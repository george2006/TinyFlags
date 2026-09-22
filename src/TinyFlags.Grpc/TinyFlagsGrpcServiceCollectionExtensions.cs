using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyFlags;

public static class TinyFlagsGrpcServiceCollectionExtensions
{
    /// <summary>
    /// Picks the gRPC transport: independent registration and real-time value push. Called from
    /// <c>AddTinyFlags</c>'s configure callback -
    /// <c>services.AddTinyFlags(tinyFlags => tinyFlags.UseGrpcTransport(...))</c>.
    /// </summary>
    public static TinyFlagsOptions UseGrpcTransport(this TinyFlagsOptions tinyFlags, Action<TinyFlagsGrpcOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(tinyFlags);
        ArgumentNullException.ThrowIfNull(configure);
        var services = tinyFlags.Services;
        var options = new TinyFlagsGrpcOptions();
        configure(options);
        var snapshot = options.CreateSnapshot();
        var existing = services.LastOrDefault(service => service.ServiceType == typeof(TinyFlagsGrpcOptions));
        if (existing is not null
            && (existing.ImplementationInstance is not TinyFlagsGrpcOptions registered || !registered.HasSameConfigurationAs(snapshot)))
        {
            throw new InvalidOperationException("TinyFlags gRPC transport is already configured with different settings.");
        }

        if (existing is null)
        {
            services.AddSingleton(snapshot);
            services.AddSingleton(provider => new TinyFlagsGrpcTransport(provider.GetRequiredService<TinyFlagsGrpcOptions>()));
            services.TryAddSingleton<IFeatureDefinitionsTransport>(provider => provider.GetRequiredService<TinyFlagsGrpcTransport>());
            services.TryAddSingleton<IFeatureValuesSubscription>(provider => provider.GetRequiredService<TinyFlagsGrpcTransport>());
        }
        services.AddHostedService<TinyFlagsRegistrationWorker>();
        services.AddHostedService<TinyFlagsValuesWatchWorker>();
        return tinyFlags;
    }
}
