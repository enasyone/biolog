using System.Collections.Concurrent;

namespace Biolog.Features.OfflineEnrolment.Domain;

/// <summary>
/// BIO-MOB-005-AC01: "Tenant policy controls which enrolment fields/biometrics may be captured
/// offline." No "TenantPolicy"/"CapturePolicy"/"AllowedFields" concept exists anywhere in this
/// repository yet (verified by repo-wide search before designing this) - this is a fresh,
/// tenant-scoped, versioned policy aggregate, shaped like BIO-ACC-001's own <c>AccessPolicy</c>/
/// BIO-ATT-001's own <c>AttendancePolicy</c> (tenant-scoped, versioned, an admin-settable
/// resource, not a bare constant).
///
/// <see cref="AllowedFields"/> keys are this feature's own closed vocabulary
/// (<see cref="KnownFields"/>) - a deliberately small, hand-picked subset of the demographic
/// fields BIO-IDN-001's own <c>Person</c> aggregate names (first/middle/last name, date of
/// birth) plus one institutional identifier field, not an open-ended arbitrary string set.
/// </summary>
public static class KnownFields
{
    public const string FirstName = "first_name";
    public const string MiddleName = "middle_name";
    public const string LastName = "last_name";
    public const string BornOn = "born_on";
    public const string InstitutionalIdentifier = "institutional_identifier";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        FirstName, MiddleName, LastName, BornOn, InstitutionalIdentifier,
    };
}

public static class KnownBiometricModalities
{
    public const string Fingerprint = "fingerprint";
    public const string Face = "face";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Fingerprint, Face,
    };
}

public sealed record EnrolmentCapturePolicy
{
    public required string TenantId { get; init; }
    public required IReadOnlySet<string> AllowedFields { get; init; }
    public required bool AllowPhotoCapture { get; init; }
    public required IReadOnlySet<string> AllowedBiometricModalities { get; init; }
    public required int Version { get; init; }
    public required string UpdatedAt { get; init; }

    /// <summary>
    /// The story's own reasonable, disclosed default for a tenant that has never explicitly
    /// configured a policy (no ADR/spec value exists for this, the same "own documented
    /// judgment call" position BIO-MOB-002's retry constants and BIO-ACC-003's stale-cache
    /// threshold were each in) - a conservative, text-only baseline matching this story's own
    /// title ("LIMITED offline enrolment"): core demographic fields only, no photo, no
    /// biometrics, until a tenant admin explicitly opts in via
    /// <c>PUT /api/v1/enrolment/capture-policy</c>.
    /// </summary>
    public static EnrolmentCapturePolicy Default(string tenantId) => new()
    {
        TenantId = tenantId,
        AllowedFields = new HashSet<string>(StringComparer.Ordinal) { KnownFields.FirstName, KnownFields.LastName, KnownFields.BornOn },
        AllowPhotoCapture = false,
        AllowedBiometricModalities = new HashSet<string>(StringComparer.Ordinal),
        Version = 0,
        UpdatedAt = Identifiers.UtcInstant.Now(),
    };
}

public interface IEnrolmentCapturePolicyRepository
{
    Task<EnrolmentCapturePolicy> GetAsync(string tenantId, CancellationToken cancellationToken = default);

    Task<EnrolmentCapturePolicy> SetAsync(string tenantId, IReadOnlySet<string> allowedFields, bool allowPhotoCapture, IReadOnlySet<string> allowedBiometricModalities, CancellationToken cancellationToken = default);
}

/// <summary>In-memory, process-lifetime store. Returns <see cref="EnrolmentCapturePolicy.Default"/> for any tenant with no explicit record - a tenant is never blocked from offline enrolment entirely just because no admin has visited the policy screen yet.</summary>
internal sealed class InMemoryEnrolmentCapturePolicyRepository : IEnrolmentCapturePolicyRepository
{
    private readonly ConcurrentDictionary<string, EnrolmentCapturePolicy> _policies = new(StringComparer.Ordinal);

    public Task<EnrolmentCapturePolicy> GetAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_policies.TryGetValue(tenantId, out var policy) ? policy : EnrolmentCapturePolicy.Default(tenantId));

    public Task<EnrolmentCapturePolicy> SetAsync(string tenantId, IReadOnlySet<string> allowedFields, bool allowPhotoCapture, IReadOnlySet<string> allowedBiometricModalities, CancellationToken cancellationToken = default)
    {
        var previousVersion = _policies.TryGetValue(tenantId, out var existing) ? existing.Version : 0;
        var updated = new EnrolmentCapturePolicy
        {
            TenantId = tenantId,
            AllowedFields = allowedFields,
            AllowPhotoCapture = allowPhotoCapture,
            AllowedBiometricModalities = allowedBiometricModalities,
            Version = previousVersion + 1,
            UpdatedAt = Identifiers.UtcInstant.Now(),
        };
        _policies[tenantId] = updated;
        return Task.FromResult(updated);
    }
}
