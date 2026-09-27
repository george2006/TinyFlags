using System;
using Microsoft.Extensions.DependencyInjection;

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
