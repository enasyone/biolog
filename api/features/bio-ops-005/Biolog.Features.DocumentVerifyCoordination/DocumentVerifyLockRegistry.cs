using System.Collections.Concurrent;

namespace Biolog.Features.DocumentVerifyCoordination;

/// <summary>
/// Per-session async mutual exclusion, mirroring BIO-INT-002's own <c>ImportLockRegistry</c> /
/// BIO-DOC-011's own <c>DocumentTemplateDraftLockRegistry</c> exactly: a self-caught lesson from
/// this epic's own history is that reordering two independently-failable operations never
/// eliminates a race, only a true mutual-exclusion lock does. <see cref="DocumentVerifyCoordinatorService.CheckAuthenticityAsync"/>
/// is the one operation here that needs it - its own caller-tracked "next expected version"
/// (<see cref="DocumentComparisonVersioningState.NextVersionNumber"/>) races BIO-DOC-008's own
/// <c>IVersionStore.AllocateVersion</c> two-phase reservation under genuine concurrency unless
/// every call for the SAME session is strictly serialized here first.
/// </summary>
public sealed class DocumentVerifyLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string documentSessionId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(documentSessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
