using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace TinyFlags;

internal static class HostApplicationLifetimeExtensions
{
    /// <summary>
    /// Completes once <see cref="IHostApplicationLifetime.ApplicationStarted"/> fires, or throws
    /// <see cref="System.OperationCanceledException"/> if <paramref name="ct"/> cancels first -
    /// including before startup finishes. <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>
    /// avoids running a caller's continuation synchronously on whatever thread fires the event.
    /// </summary>
    public static async Task WaitForApplicationStartedAsync(this IHostApplicationLifetime lifetime, CancellationToken ct)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var signal = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(ct).ConfigureAwait(false);
    }
}
