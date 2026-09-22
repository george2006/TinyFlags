using System;

namespace TinyFlags;

/// <summary>
/// How often <see cref="TinyFlagsSynchronizationWorker"/> polls a pull transport. Only meaningful
/// for pull transports - a push subscription has no polling cadence of its own.
/// </summary>
public sealed class FeatureValuesPollingOptions
{
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}
