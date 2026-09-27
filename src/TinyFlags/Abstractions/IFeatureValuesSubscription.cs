using System.Collections.Generic;
using System.Threading;

namespace TinyFlags;

/// <summary>
/// Streams full value snapshots for an environment, handling transient reconnects internally.
/// Implementations must not yield <see cref="FeatureValuesResult.Unchanged"/>.
/// </summary>
public interface IFeatureValuesSubscription
{
    IAsyncEnumerable<FeatureValuesResult> WatchAsync(IReadOnlyList<FeatureDefinition> catalog, CancellationToken ct = default);
}
