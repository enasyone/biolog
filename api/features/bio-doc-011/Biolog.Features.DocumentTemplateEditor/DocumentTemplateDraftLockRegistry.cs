using System.Collections.Concurrent;

namespace Biolog.Features.DocumentTemplateEditor;

/// <summary>
/// Per-<c>(TenantId, TemplateId)</c> in-process locking (ADR-0003 Decision 7's audit-atomicity
/// pattern, mirroring BIO-INT-002's own <c>ImportLockRegistry</c>) - keyed by the whole lineage,
/// not one version row, so a mutation to one version and a concurrent
/// <see cref="DocumentTemplateDraftService.CreateNextVersionAsync"/> on the same lineage can never
/// race each other. Registered Singleton so the lock table outlives any one request; the service
/// itself stays Scoped.
///
/// Round-2 fix (Zainab, ZB-DOC011-02, High): round 1 relied on an optimistic-concurrency
/// <c>TryUpdate(expectedRevision)</c> at the repository layer with no real serialization, which
/// left two distinct ordering bugs (audit-before-persist-fails, and a raw <c>Max()+1</c> race in
/// <c>CreateNextVersionAsync</c>). Serializing every mutation to a lineage through this lock
/// removes the race entirely, which is what lets the service audit BEFORE committing again
/// (ADR-0003 Decision 7) without the risk of auditing a mutation that a concurrent writer then
/// clobbers or that never actually lands.
/// </summary>
public sealed class DocumentTemplateDraftLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, string TemplateId), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, string templateId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, templateId), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
