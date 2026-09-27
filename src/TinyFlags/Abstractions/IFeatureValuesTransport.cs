using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TinyFlags;

/// <summary>
/// Retrieves a full snapshot or an unchanged result relative to the accepted cursor.
/// The cursor is null before the first accepted snapshot. Each call performs one synchronization;
/// implementations handle transient retries internally.
/// </summary>
public interface IFeatureValuesTransport
{
    Task<FeatureValuesResult> GetValuesAsync(IReadOnlyList<FeatureDefinition> catalog,
        FeatureValuesCursor? current, CancellationToken ct = default);
}
