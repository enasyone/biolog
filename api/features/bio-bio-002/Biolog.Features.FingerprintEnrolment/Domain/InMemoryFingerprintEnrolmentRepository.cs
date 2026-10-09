using System.Collections.Concurrent;

namespace Biolog.Features.FingerprintEnrolment.Domain;

/// <summary>In-process, in-memory <see cref="IFingerprintEnrolmentRepository"/> for local
/// development and this story's own tests - explicitly NOT a real persistence layer (mirrors
/// BIO-BIO-001's <c>InMemoryBiometricTemplateStore</c> scope note). Internal, same seam as the
/// interface - re-validates tenant scoping and identity/tenant immutability at the persistence
/// boundary independently of <see cref="Service.FingerprintEnrolmentService"/>, the same
/// dual-boundary discipline BIO-IDN-001-QA-01 established.</summary>
internal sealed class InMemoryFingerprintEnrolmentRepository : IFingerprintEnrolmentRepository
{
    private readonly ConcurrentDictionary<string, FingerprintEnrolmentSession> _sessions = new(StringComparer.Ordinal);

    public Task<FingerprintEnrolmentSession> CreateAsync(FingerprintEnrolmentSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Version != 1)
        {
            throw new InvalidOperationException($"invalid_initial_version: session '{session.Id}' must start at version 1.");
        }
        if (!_sessions.TryAdd(session.Id, session))
        {
            throw new InvalidOperationException($"fingerprint_enrolment_session_already_exists: '{session.Id}'.");
        }
        return Task.FromResult(session);
    }

    public Task<FingerprintEnrolmentSession?> GetAsync(string tenantId, string sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.TenantId != tenantId)
        {
            // Cross-tenant lookup returns null, not an exception - ADR-0001 §1.5, no
            // enumeration oracle distinguishing "does not exist" from "belongs to another tenant".
            return Task.FromResult<FingerprintEnrolmentSession?>(null);
        }
        return Task.FromResult<FingerprintEnrolmentSession?>(session);
    }

    public Task<FingerprintEnrolmentSession> ReplaceAsync(
        FingerprintEnrolmentSession expected, FingerprintEnrolmentSession updated, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(updated);
        if (updated.Id != expected.Id || updated.TenantId != expected.TenantId || updated.PersonId != expected.PersonId)
        {
            throw new InvalidOperationException("identity_or_tenant_immutable: a replace cannot change id/tenant/person.");
        }
        if (!_sessions.TryGetValue(expected.Id, out var current) || current.TenantId != expected.TenantId)
        {
            throw FingerprintEnrolmentException.SessionNotFound(expected.Id);
        }
        if (current.Version != expected.Version)
        {
            throw FingerprintEnrolmentException.ConcurrencyConflict(expected.Id, expected.Version, current.Version);
        }
        _sessions[updated.Id] = updated;
        return Task.FromResult(updated);
    }

    public Task<bool> DeleteAsync(string tenantId, string sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var current) || current.TenantId != tenantId)
        {
            return Task.FromResult(false);
        }
        return Task.FromResult(_sessions.TryRemove(sessionId, out _));
    }
}
