using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace TinyFlags;

public static class ServiceCollectionSingletonExtensions
{
    /// <summary>
    /// Registers <paramref name="instance"/> if absent and returns true when added.
    /// Returns false for an existing equivalent instance; otherwise throws
    /// <see cref="InvalidOperationException"/> with <paramref name="conflictMessage"/>.
    /// </summary>
    public static bool RegisterSingletonOnce<T>(this IServiceCollection services, T instance,
        Func<T, T, bool> hasSameConfigurationAs, string conflictMessage) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(hasSameConfigurationAs);
        ArgumentException.ThrowIfNullOrWhiteSpace(conflictMessage);

        var existing = services.LastOrDefault(service => service.ServiceType == typeof(T));
        if (existing is null)
        {
            services.AddSingleton(instance);
            return true;
        }

        if (existing.ImplementationInstance is not T registered || !hasSameConfigurationAs(registered, instance))
        {
            throw new InvalidOperationException(conflictMessage);
        }
        return false;
    }
}
