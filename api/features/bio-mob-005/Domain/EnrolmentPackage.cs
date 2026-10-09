using System.Collections.Concurrent;

namespace Biolog.Features.OfflineEnrolment.Domain;

/// <summary>
/// BIO-MOB-005-AC03/AC04's lifecycle for a submitted enrolment package. <c>Submitted</c> is the
/// intake state before the central duplicate scan has run; it always transitions synchronously,
/// within the same intake request, to either <c>Accepted</c> (no signal match) or
/// <c>PendingReview</c> (a signal match raised an <see cref="EnrolmentDuplicateCandidate"/> - see
/// that type's header). <c>PendingReview</c> resolves only via a human decision
/// (<see cref="EnrolmentPackageService.DecideDuplicateCandidateAsync"/>) to either
/// <c>Accepted</c> or <c>RejectedDuplicate</c> - both terminal.
/// </summary>
public enum EnrolmentPackageStatus
{
    Submitted,
    PendingReview,
    Accepted,
    RejectedDuplicate,
}

public static class EnrolmentPackageStatusExtensions
{
    public static string ToWireString(this EnrolmentPackageStatus status) => status switch
    {
        EnrolmentPackageStatus.Submitted => "SUBMITTED",
        EnrolmentPackageStatus.PendingReview => "PENDING_REVIEW",
        EnrolmentPackageStatus.Accepted => "ACCEPTED",
        EnrolmentPackageStatus.RejectedDuplicate => "REJECTED_DUPLICATE",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// A device's offline-captured enrolment submission, once it has reached the server. Only
/// fields/biometrics the tenant's <see cref="EnrolmentCapturePolicy"/> permitted at submission
/// time are ever populated here - enforced both client-side (the mobile capture screen only
/// shows/enables policy-permitted inputs) and server-side
/// (<see cref="EnrolmentPackageService.SubmitPackageAsync"/> rejects any field/modality the
/// resolved tenant's current policy does not allow), the same "validated at the request
/// boundary AND the repository boundary" discipline <c>Person.cs</c>'s own header documents.
///
/// <b>Materializing an <c>Accepted</c> package into a real BIO-IDN-001 <c>Person</c> record is
/// explicitly out of scope</b> - PersonRegistry/BIO-IDN-001 is not a declared dependency of this
/// story. This story delivers the offline capture, encrypted local queue, deterministic sync,
/// central duplicate detection and human resolution queue (AC01-AC04); a future story consumes
/// <c>Accepted</c> packages to create the real person record, the same disclosed-scope-boundary
/// pattern BIO-ACC-003 used for card-level revocation (BIO-CRD-006 not a declared dependency).
/// </summary>
public sealed record EnrolmentPackage
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string DeviceId { get; init; }
    public required IReadOnlyDictionary<string, string> FieldValues { get; init; }
    public required bool PhotoCaptured { get; init; }
    public string? PhotoContentBase64 { get; init; }
    public required IReadOnlyList<string> BiometricModalitiesCaptured { get; init; }
    public required IReadOnlyDictionary<string, string> BiometricPayloadsBase64 { get; init; }
    public required EnrolmentPackageStatus Status { get; init; }
    public required string SubmittedAt { get; init; }
    public string? DecidedAt { get; init; }
    public string? DecisionReasonCode { get; init; }
}

public interface IEnrolmentPackageRepository
{
    Task<EnrolmentPackage?> GetAsync(string tenantId, string packageId, CancellationToken cancellationToken = default);

    Task CreateAsync(EnrolmentPackage package, CancellationToken cancellationToken = default);

    Task ReplaceAsync(EnrolmentPackage package, CancellationToken cancellationToken = default);

    /// <summary>Every already-decided (<c>Accepted</c>) package in this tenant - the duplicate-scan candidate pool (AC03). A <c>PendingReview</c> or <c>RejectedDuplicate</c> package is never itself a match target: the former has not yet been confirmed distinct, the latter was already confirmed a duplicate and struck from consideration.</summary>
    Task<IReadOnlyList<EnrolmentPackage>> ListAcceptedAsync(string tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EnrolmentPackage>> ListByStatusAsync(string tenantId, EnrolmentPackageStatus status, CancellationToken cancellationToken = default);
}

internal sealed class InMemoryEnrolmentPackageRepository : IEnrolmentPackageRepository
{
    private readonly ConcurrentDictionary<string, EnrolmentPackage> _packages = new(StringComparer.Ordinal);

    public Task<EnrolmentPackage?> GetAsync(string tenantId, string packageId, CancellationToken cancellationToken = default)
    {
        var found = _packages.TryGetValue(packageId, out var package) && package.TenantId == tenantId ? package : null;
        return Task.FromResult(found);
    }

    public Task CreateAsync(EnrolmentPackage package, CancellationToken cancellationToken = default)
    {
        _packages[package.Id] = package;
        return Task.CompletedTask;
    }

    public Task ReplaceAsync(EnrolmentPackage package, CancellationToken cancellationToken = default)
    {
        _packages[package.Id] = package;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EnrolmentPackage>> ListAcceptedAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EnrolmentPackage> items = [.. _packages.Values.Where(p => p.TenantId == tenantId && p.Status == EnrolmentPackageStatus.Accepted)];
        return Task.FromResult(items);
    }

    public Task<IReadOnlyList<EnrolmentPackage>> ListByStatusAsync(string tenantId, EnrolmentPackageStatus status, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EnrolmentPackage> items = [.. _packages.Values
            .Where(p => p.TenantId == tenantId && p.Status == status)
            .OrderBy(p => p.SubmittedAt, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }
}
