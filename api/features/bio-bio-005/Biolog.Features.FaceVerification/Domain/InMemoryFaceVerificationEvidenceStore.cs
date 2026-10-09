using System.Collections.Concurrent;

namespace Biolog.Features.FaceVerification.Domain;

/// <summary>In-process, in-memory <see cref="IFaceVerificationEvidenceStore"/> for local
/// development and this story's own tests - explicitly NOT a real persistence layer. Internal,
/// same seam as the interface.</summary>
internal sealed class InMemoryFaceVerificationEvidenceStore : IFaceVerificationEvidenceStore
{
    private readonly ConcurrentDictionary<string, FaceVerificationEvidence> _evidence = new(StringComparer.Ordinal);

    public Task SaveAsync(FaceVerificationEvidence evidence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!_evidence.TryAdd(evidence.Id, evidence))
        {
            throw new InvalidOperationException($"face_verification_evidence_already_exists: '{evidence.Id}'.");
        }
        return Task.CompletedTask;
    }

    public Task<FaceVerificationEvidence?> GetAsync(string tenantId, string evidenceId, CancellationToken cancellationToken)
    {
        if (!_evidence.TryGetValue(evidenceId, out var evidence) || evidence.TenantId != tenantId)
        {
            // Cross-tenant lookup returns null, not an exception - ADR-0001 §1.5, no
            // enumeration oracle distinguishing "does not exist" from "belongs to another tenant".
            return Task.FromResult<FaceVerificationEvidence?>(null);
        }
        return Task.FromResult<FaceVerificationEvidence?>(evidence);
    }

    public Task<IReadOnlyList<FaceVerificationEvidence>> GetForPersonAsync(
        string tenantId, string personId, CancellationToken cancellationToken)
    {
        IReadOnlyList<FaceVerificationEvidence> matches = _evidence.Values
            .Where(e => e.TenantId == tenantId && e.PersonId == personId)
            .OrderByDescending(e => e.OccurredAtUtc, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(matches);
    }
}
