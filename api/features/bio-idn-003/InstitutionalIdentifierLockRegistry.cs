using System.Collections.Concurrent;

namespace Biolog.Features.InstitutionalIdentifiers;

/// <summary>
/// Per-<c>(PersonId, Type, CustomLabel)</c> in-process locking, held in its OWN singleton class
/// rather than as instance state on <see cref="InstitutionalIdentifierService"/> - identical
/// reasoning to BIO-IDN-001's <c>PersonLockRegistry</c> and BIO-IDN-002's
/// <c>PersonProfileLockRegistry</c>: <see cref="InstitutionalIdentifierService"/> reads a
/// per-request <see cref="Tenancy.ICurrentTenantProvider"/>, so it must be registered Scoped,
/// and a Singleton cannot safely capture a Scoped dependency.
/// </summary>
public sealed class InstitutionalIdentifierLockRegistry
{
    private readonly ConcurrentDictionary<(string PersonId, Domain.IdentifierType Type, string? CustomLabel), SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string personId, Domain.IdentifierType type, string? customLabel, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd((personId, type, customLabel), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
