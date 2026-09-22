using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TinyFlags;

/// <summary>
/// Pulls the current values for an environment, conditionally on a cursor already held locally.
/// Called on a recurring interval by <see cref="TinyFlagsSynchronizationWorker"/>, which owns the
/// polling loop, jitter and backoff itself — this contract only answers "what's current now."
/// </summary>
public interface IFeatureValuesTransport
{
    Task<FeatureValuesResult> GetValuesAsync(IReadOnlyList<FeatureDefinition> catalog,
        FeatureValuesCursor? current, CancellationToken ct = default);
}
