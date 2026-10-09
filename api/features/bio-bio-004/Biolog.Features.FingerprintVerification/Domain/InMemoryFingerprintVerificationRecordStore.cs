using System.Collections.Concurrent;

namespace Biolog.Features.FingerprintVerification.Domain;

/// <summary>In-process, in-memory <see cref="IFingerprintVerificationRecordStore"/> for local
/// development and this story's own tests - explicitly NOT a real persistence layer (mirrors
/// BIO-BIO-001's <c>InMemoryBiometricTemplateStore</c> scope note). Internal, same seam as the
/// interface.</summary>
internal sealed class InMemoryFingerprintVerificationRecordStore : IFingerprintVerificationRecordStore
{
    private readonly ConcurrentDictionary<string, FingerprintVerificationRecord> _records = new(StringComparer.Ordinal);

    public Task SaveAsync(FingerprintVerificationRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!_records.TryAdd(record.Id, record))
        {
            throw new InvalidOperationException($"fingerprint_verification_record_already_exists: '{record.Id}'.");
        }
        return Task.CompletedTask;
    }

    public Task<FingerprintVerificationRecord?> GetAsync(string tenantId, string recordId, CancellationToken cancellationToken)
    {
        if (!_records.TryGetValue(recordId, out var record) || record.TenantId != tenantId)
        {
            // Cross-tenant lookup returns null, not an exception - ADR-0001 §1.5, no
            // enumeration oracle distinguishing "does not exist" from "belongs to another tenant".
            return Task.FromResult<FingerprintVerificationRecord?>(null);
        }
        return Task.FromResult<FingerprintVerificationRecord?>(record);
    }

    public Task<IReadOnlyList<FingerprintVerificationRecord>> GetForPersonAsync(
        string tenantId, string personId, CancellationToken cancellationToken)
    {
        IReadOnlyList<FingerprintVerificationRecord> matches = _records.Values
            .Where(r => r.TenantId == tenantId && r.PersonId == personId)
            .OrderByDescending(r => r.OccurredAtUtc, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(matches);
    }
}
