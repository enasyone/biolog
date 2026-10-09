using System.Collections.Concurrent;

namespace Biolog.Features.OfflineEnrolment.Domain;

/// <summary>The signal that raised a candidate - mirrors the shape of BIO-IDN-005's own <c>DuplicateSignalSource</c> (name+dob / institutional identifier), an independent, project-local re-derivation, not a shared type (BIO-IDN-005's own service is <c>internal</c> and not a declared dependency of this story - see this file's own header and BIO-BIO-006's own precedent for building an independent queue in that exact situation).</summary>
public enum DuplicateSignalSource
{
    NameAndDateOfBirth,
    InstitutionalIdentifier,
}

public enum DuplicateCandidateStatus
{
    PendingReview,
    ConfirmedDuplicate,
    ConfirmedDistinct,
}

public static class DuplicateCandidateStatusExtensions
{
    public static string ToWireString(this DuplicateCandidateStatus status) => status switch
    {
        DuplicateCandidateStatus.PendingReview => "PENDING_REVIEW",
        DuplicateCandidateStatus.ConfirmedDuplicate => "CONFIRMED_DUPLICATE",
        DuplicateCandidateStatus.ConfirmedDistinct => "CONFIRMED_DISTINCT",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}

public static class DuplicateSignalSourceExtensions
{
    public static string ToWireString(this DuplicateSignalSource source) => source switch
    {
        DuplicateSignalSource.NameAndDateOfBirth => "NAME_AND_DATE_OF_BIRTH",
        DuplicateSignalSource.InstitutionalIdentifier => "INSTITUTIONAL_IDENTIFIER",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// BIO-MOB-005-AC03/AC04: raised by <see cref="EnrolmentDuplicateReviewService"/> when a newly
/// submitted package's fields signal-match an already-<c>Accepted</c> package in the same
/// tenant. Holds the new package in <see cref="EnrolmentPackageStatus.PendingReview"/>
/// (AC04: "security conflicts require human resolution" - ADR-0001 §3.5's own "no silent
/// last-write-wins for security-sensitive data," applied here to identity data, not sync
/// conflicts) until <see cref="EnrolmentPackageService.DecideDuplicateCandidateAsync"/> records
/// a human decision with a mandatory <c>reason_code</c> and actor.
/// </summary>
public sealed record EnrolmentDuplicateCandidate
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string PackageId { get; init; }
    public required string MatchedPackageId { get; init; }
    public required DuplicateSignalSource SignalSource { get; init; }
    public required DuplicateCandidateStatus Status { get; init; }
    public required string RaisedAt { get; init; }
    public string? DecidedAt { get; init; }
    public string? DecidedByActorId { get; init; }
    public string? ReasonCode { get; init; }
}

public interface IEnrolmentDuplicateCandidateRepository
{
    Task<EnrolmentDuplicateCandidate?> GetAsync(string tenantId, string candidateId, CancellationToken cancellationToken = default);

    Task CreateAsync(EnrolmentDuplicateCandidate candidate, CancellationToken cancellationToken = default);

    Task ReplaceAsync(EnrolmentDuplicateCandidate candidate, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EnrolmentDuplicateCandidate>> ListByStatusAsync(string tenantId, DuplicateCandidateStatus status, CancellationToken cancellationToken = default);
}

internal sealed class InMemoryEnrolmentDuplicateCandidateRepository : IEnrolmentDuplicateCandidateRepository
{
    private readonly ConcurrentDictionary<string, EnrolmentDuplicateCandidate> _candidates = new(StringComparer.Ordinal);

    public Task<EnrolmentDuplicateCandidate?> GetAsync(string tenantId, string candidateId, CancellationToken cancellationToken = default)
    {
        var found = _candidates.TryGetValue(candidateId, out var candidate) && candidate.TenantId == tenantId ? candidate : null;
        return Task.FromResult(found);
    }

    public Task CreateAsync(EnrolmentDuplicateCandidate candidate, CancellationToken cancellationToken = default)
    {
        _candidates[candidate.Id] = candidate;
        return Task.CompletedTask;
    }

    public Task ReplaceAsync(EnrolmentDuplicateCandidate candidate, CancellationToken cancellationToken = default)
    {
        _candidates[candidate.Id] = candidate;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EnrolmentDuplicateCandidate>> ListByStatusAsync(string tenantId, DuplicateCandidateStatus status, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EnrolmentDuplicateCandidate> items = [.. _candidates.Values
            .Where(c => c.TenantId == tenantId && c.Status == status)
            .OrderBy(c => c.RaisedAt, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }
}
