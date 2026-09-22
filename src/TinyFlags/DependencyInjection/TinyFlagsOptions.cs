using Microsoft.Extensions.DependencyInjection;

namespace TinyFlags;

/// <summary>
/// Passed to <see cref="TinyFlagsServiceCollectionExtensions.AddTinyFlags"/>'s configure callback.
/// A transport package (e.g. TinyFlags.Http) contributes its own extension method on this type -
/// <c>tinyFlags.UseHttpTransport(...)</c> - registering directly against <see cref="Services"/>,
/// so core never needs to know a given transport exists.
/// </summary>
public sealed class TinyFlagsOptions
{
    public IServiceCollection Services { get; }

    internal TinyFlagsOptions(IServiceCollection services) => Services = services;
}
