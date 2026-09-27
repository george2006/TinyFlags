using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TinyFlags;

/// <summary>
/// Sends the local flag catalog to a server. Called once per host lifetime, after startup.
/// </summary>
public interface IFeatureDefinitionsTransport
{
    Task RegisterAsync(IReadOnlyList<FeatureDefinition> definitions, CancellationToken ct = default);
}
