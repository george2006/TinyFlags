using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyFlags;

public static class TinyFlagsServiceCollectionExtensions
{
    /// <summary>
    /// Registers a shared local store and applies contributions from initialized assemblies. No
    /// transport - local-only mode until a transport package (e.g. TinyFlags.Http) is also added.
    /// </summary>
    public static IServiceCollection AddTinyFlags(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<FeatureValues>();
        TinyFlagsBootstrap.Apply(services);
        return services;
    }
}
