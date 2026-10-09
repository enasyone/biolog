namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>The score at or above which a ranked candidate (AC01) is flagged
/// <see cref="FingerprintSearchCandidate.PossibleDuplicate"/> and routed to human review (AC03).
/// Tenant-keyed, resolved by <see cref="Service.FingerprintSearchService"/> itself - never a
/// per-request caller input - the same "policy resolves it, not an arbitrary parameter" shape
/// BIO-BIO-005-AC02 established for its own threshold.</summary>
public interface IFingerprintDuplicateThresholdPolicy
{
    double GetThreshold(string tenantId);
}
