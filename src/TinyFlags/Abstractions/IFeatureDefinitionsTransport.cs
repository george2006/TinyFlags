using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TinyFlags;

/// <summary>
/// Sends the locally-declared flag catalog to a server. Implemented once per host lifetime, called
/// once at startup by <see cref="TinyFlagsRegistrationWorker"/> — there is no pull or push variant,
/// registration is inherently a single request/response write.
/// </summary>
public interface IFeatureDefinitionsTransport
{
    Task RegisterAsync(IReadOnlyList<FeatureDefinition> definitions, CancellationToken ct = default);
}
