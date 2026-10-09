using System.Collections.Concurrent;

namespace Biolog.Features.CardIssuance;

/// <summary>
/// Per-(tenant, person, card class) in-process locking, held in its own singleton class rather
/// than as instance state on <see cref="CardIssueService"/> itself - the same split
/// <c>PersonLockRegistry</c> (BIO-IDN-001) uses for the identical reason:
/// <see cref="CardIssueService"/> reads a per-request <c>Tenancy.ICurrentTenantProvider</c>, so
/// it must be registered Scoped, and a Singleton cannot safely capture a Scoped dependency.
///
/// Keyed by the SAME (tenant, person, card class) triple <see cref="CardIssueStatus.Active"/>'s
/// uniqueness invariant (BIO-CRD-005-AC04) is scoped to - serialising every mutation against
/// that key (issue/encode/print/verify/activate/deactivate) is what makes
/// <see cref="CardIssueService.ActivateAsync"/>'s "check no sibling is Active, then set Active"
/// sequence genuinely transactional within this process, not merely check-then-hope.
/// </summary>
public sealed class CardIssueLockRegistry
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string tenantId, string personId, string cardClass, CancellationToken cancellationToken)
    {
        var key = $"{tenantId}\u0000{personId}\u0000{cardClass}";
        var gate = _locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
