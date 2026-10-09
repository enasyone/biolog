using System.Collections.Concurrent;

namespace Biolog.Features.FaceEnrolment.Domain;

/// <summary>In-process, in-memory <see cref="IFaceBiometricReferenceRepository"/> for local
/// development and this story's own tests - explicitly NOT a real persistence layer (mirrors
/// BIO-BIO-001's <c>InMemoryBiometricTemplateStore</c> scope note). Internal, same seam as the
/// interface.</summary>
internal sealed class InMemoryFaceBiometricReferenceRepository : IFaceBiometricReferenceRepository
{
    private readonly ConcurrentDictionary<string, FaceBiometricReference> _byId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string TenantId, string PersonId), string> _latestIdByPerson = new();

    public Task SaveAsync(FaceBiometricReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        _byId[reference.Id] = reference;
        _latestIdByPerson[(reference.TenantId, reference.PersonId)] = reference.Id;
        return Task.CompletedTask;
    }

    public Task<FaceBiometricReference?> GetAsync(string tenantId, string referenceId, CancellationToken cancellationToken)
    {
        if (!_byId.TryGetValue(referenceId, out var reference) || reference.TenantId != tenantId)
        {
            // Cross-tenant lookup returns null, not an exception - ADR-0001 §1.5, no
            // enumeration oracle.
            return Task.FromResult<FaceBiometricReference?>(null);
        }
        return Task.FromResult<FaceBiometricReference?>(reference);
    }

    public Task<FaceBiometricReference?> GetLatestForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken)
    {
        if (!_latestIdByPerson.TryGetValue((tenantId, personId), out var id))
        {
            return Task.FromResult<FaceBiometricReference?>(null);
        }
        return GetAsync(tenantId, id, cancellationToken);
    }

    public Task<bool> DeleteAsync(string tenantId, string referenceId, CancellationToken cancellationToken)
    {
        if (!_byId.TryGetValue(referenceId, out var reference) || reference.TenantId != tenantId)
        {
            return Task.FromResult(false);
        }
        return Task.FromResult(_byId.TryRemove(referenceId, out _));
    }
}
