using System;

namespace TinyFlags;

/// <summary>
/// Configures the refresh interval for pull transports.
/// </summary>
public sealed class FeatureValuesPollingOptions
{
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}
