using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyFlags;

public static class TinyFlagsServiceCollectionExtensions
{
    /// <summary>
    /// Registers a shared local store and applies contributions from initialized assemblies.
    /// Local-only until <paramref name="configure"/> picks a transport, via an extension method a
    /// transport package (e.g. TinyFlags.Http's <c>UseHttpTransport</c>) contributes on
    /// <see cref="TinyFlagsOptions"/> - the same entry point regardless of which transport you use.
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
