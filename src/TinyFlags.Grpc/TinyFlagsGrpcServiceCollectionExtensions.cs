using System;
using Microsoft.Extensions.DependencyInjection;

namespace TinyFlags;

public static class TinyFlagsGrpcServiceCollectionExtensions
{
    /// <summary>
    /// Uses gRPC for independent definitions registration and value subscriptions after host startup.
    /// </summary>
    public static TinyFlagsOptions UseGrpcTransport(this TinyFlagsOptions tinyFlags, Action<TinyFlagsGrpcOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(tinyFlags);
        ArgumentNullException.ThrowIfNull(configure);
        var services = tinyFlags.Services;
        var options = new TinyFlagsGrpcOptions();
        configure(options);
        var snapshot = options.CreateSnapshot();

        if (services.RegisterSingletonOnce(snapshot, static (a, b) => a.HasSameConfigurationAs(b),
                "TinyFlags gRPC transport is already configured with different settings."))
        {
            services.AddSingleton(provider => new TinyFlagsGrpcTransport(provider.GetRequiredService<TinyFlagsGrpcOptions>()));
        }
        return tinyFlags
            .UseDefinitionsTransport<TinyFlagsGrpcTransport>()
            .UsePushTransport<TinyFlagsGrpcTransport>();
    }
}
