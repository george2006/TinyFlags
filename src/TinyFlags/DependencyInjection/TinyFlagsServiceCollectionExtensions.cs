using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyFlags;

public static class TinyFlagsServiceCollectionExtensions
{
    /// <summary>
    /// Registers a shared local store and applies contributions from initialized assemblies.
    /// Operates locally unless <paramref name="configure"/> selects a transport.
    /// </summary>
    public static IServiceCollection AddTinyFlags(this IServiceCollection services, Action<TinyFlagsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<FeatureValues>();
        TinyFlagsBootstrap.Apply(services);
        configure?.Invoke(new TinyFlagsOptions(services));
        return services;
    }
}
