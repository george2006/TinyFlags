using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

public static class TinyFlagsHttpServiceCollectionExtensions
{
    /// <summary>
    /// Uses HTTP for independent definitions registration and value polling after host startup.
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
        }
        return tinyFlags
            .UseDefinitionsTransport<TinyFlagsApiClient>()
            .UsePullTransport<TinyFlagsApiClient>();
    }
}
