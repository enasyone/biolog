using System.Collections.Concurrent;

namespace Biolog.Features.InstitutionalIdentifiers;

/// <summary>
/// Per-<c>(TenantId, Type, CustomLabel)</c> in-process locking, shared by
/// <see cref="IdentifierUniquenessRuleService.SetRuleAsync"/> and
/// <see cref="InstitutionalIdentifierService.AssignAsync"/> (BIO-IDN-003-QA-01, Seyi round 1).
/// This is the SAME scope AC02's uniqueness invariant spans - "the same enforced-unique value
/// may not be assigned to two different people of the same type" is a fact about the whole
/// <c>(TenantId, Type, CustomLabel)</c> population, not about any one person's own record, so
/// the lock that protects it must be keyed the same way, independently of
/// <see cref="InstitutionalIdentifierLockRegistry"/>'s narrower per-person key (the same
/// two-lock-scope shape <c>TenantDomainService</c>, BIO-TEN-002, established for an identical
/// "global collision invariant vs. single-record mutation" split). A rule change and an
/// in-flight assignment for the same type are therefore also fully serialized against each
/// other - the same lock, the same key. Own singleton class for the same captive-dependency
/// reason as every sibling lock registry in this codebase.
/// </summary>
public sealed class IdentifierUniquenessRuleLockRegistry
{
    private readonly ConcurrentDictionary<(string TenantId, Domain.IdentifierType Type, string? CustomLabel), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string tenantId, Domain.IdentifierType type, string? customLabel, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((tenantId, type, customLabel), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
