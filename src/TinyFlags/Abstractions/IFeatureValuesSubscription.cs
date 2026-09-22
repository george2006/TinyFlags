using System.Collections.Generic;
using System.Threading;

namespace TinyFlags;

/// <summary>
/// Streams value changes for an environment as they happen, instead of being polled for them.
/// Drained by <see cref="TinyFlagsValuesWatchWorker"/>. A well-behaved implementation only ever
/// yields real changes - it should never yield <see cref="FeatureValuesResult.Unchanged"/>, since
/// there is nothing to report until something actually changes.
/// </summary>
public interface IFeatureValuesSubscription
{
    IAsyncEnumerable<FeatureValuesResult> WatchAsync(IReadOnlyList<FeatureDefinition> catalog, CancellationToken ct = default);
}
