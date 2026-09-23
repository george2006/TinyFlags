using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace TinyFlags;

public static class ServiceCollectionSingletonExtensions
{
    /// <summary>
    /// Registers <paramref name="instance"/> as the singleton service for <typeparamref name="T"/>
    /// if none is registered yet. If one already is, it must be equivalent per
    /// <paramref name="hasSameConfigurationAs"/> - a second call with different configuration
    /// throws <paramref name="conflictMessage"/> instead of silently replacing or duplicating it.
    /// Returns whether this call performed the registration, so a caller can gate any dependent
    /// registrations on "was this the first call." Generic over any singleton, not transport-specific -
    /// used by <c>UseHttpTransport</c>/<c>UseGrpcTransport</c> to make a repeated, equivalent
    /// <c>AddTinyFlags</c> call safe, and available to any third-party transport for the same reason.
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
