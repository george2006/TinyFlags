using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace TinyFlags;

internal static class HostApplicationLifetimeExtensions
{
    public static async Task WaitForApplicationStartedAsync(this IHostApplicationLifetime lifetime, CancellationToken ct)
    {
        // Keep transport continuations off the thread signalling host startup.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var signal = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(ct).ConfigureAwait(false);
    }
}
