using System.Text.RegularExpressions;
using Biolog.Features.DuplicateReview.Api;
using Biolog.Features.DuplicateReview.Audit;
using Biolog.Features.DuplicateReview.Domain;
using Biolog.Features.DuplicateReview.Identifiers;
using Biolog.Features.DuplicateReview.Tenancy;

namespace Biolog.Features.DuplicateReview;

/// <summary>
/// BIO-IDN-005-QA-01's fix. Application service for <see cref="PersonContactPoint"/> - the
/// contact data enrolment staff record specifically for duplicate-matching purposes (see that
/// type's header). Implements the identical ADR-0003 Decision 7 audit-atomicity discipline every
/// other mutating service in this project applies: a per-person lock
/// (<see cref="PersonContactPointLockRegistry"/>), audit-before-commit, <c>ExistsAsync</c>
/// verification on an ambiguous audit-write throw, <see cref="CancellationToken.None"/> on the
/// commit once the audit write is confirmed.
///
/// Reuses <see cref="DuplicateCandidateAuditEventBuilder"/>/<see cref="IDuplicateCandidateAuditSink"/>
/// rather than standing up parallel audit plumbing - one unified audit trail for this whole
/// feature, distinguished by <see cref="Audit.AuditTarget.Type"/> ("person_contact_point" here,
/// "duplicate_candidate" for <see cref="DuplicateReviewService"/>).
///
/// <b>Audit trail privacy.</b> Email and phone are personal data by the same reasoning
/// <c>contracts/schemas/data-classification.json</c> already applies to name/date-of-birth. The
/// actual <see cref="PersonContactPoint.Email"/>/<see cref="PersonContactPoint.Phone"/> values
/// are NEVER placed into an audit event's <c>Detail</c> - both field names are listed in
/// <c>Withheld</c> unconditionally, mirroring BIO-IDN-001's identical posture. Because Detail
/// therefore never carries a "personal"-classified value, <see cref="DuplicateCandidateAuditEventBuilder"/>
/// does not need to register that classification rank at all - see that builder's own header.
///
/// <b>Existence check.</b> Applying BIO-IDN-002-QA-01's lesson: <see cref="SetAsync"/> proves the
/// target person exists in the resolved tenant, through the SAME <see cref="IPersonAttributeGateway"/>
/// <see cref="DuplicateReviewService"/> already uses, before the lock is even acquired - an
/// orphan or cross-tenant contact-point record is never created.
/// </summary>
internal sealed partial class PersonContactPointService(
    ICurrentTenantProvider tenantProvider,
    IPersonContactPointRepository repository,
    IDuplicateCandidateAuditSink auditSink,
    PersonContactPointLockRegistry lockRegistry,
    IPersonAttributeGateway personGateway)
{
    private static readonly IReadOnlyList<string> ContactFieldNames = ["email", "phone"];

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"[\x00-\x1F\x7F]")]
    private static partial Regex ControlCharacterPattern();

    public async Task<PersonContactPoint> SetAsync(
        string personId, SetContactPointRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var errors = new List<ValidationErrorItem>();
        if (!CanonicalId.IsPersonId(personId))
        {
            errors.Add(new ValidationErrorItem("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier"));
        }

        var email = ValidateEmail(request.Email, errors);
        var phone = ValidatePhone(request.Phone, errors);
        ThrowIfInvalid(errors);

        // Applying BIO-IDN-002-QA-01 from the start: prove the PERSON exists in the resolved
        // tenant before anything else - the same gateway ScanForPersonAsync already uses.
        if (await personGateway.GetAsync(personId, cancellationToken) is null)
        {
            throw DuplicateCandidateError.PersonNotFound(personId);
        }

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cancellationToken);
        var current = await repository.GetAsync(tenantId, personId, cancellationToken);

        if (current is null)
        {
            if (request.Version is not null)
            {
                errors.Add(new ValidationErrorItem("version", "invalid_format", "version must be omitted when no contact point exists yet"));
                ThrowIfInvalid(errors);
            }

            var now = UtcInstant.Now();
            var created = new PersonContactPoint(tenantId, personId) { Email = email, Phone = phone, Version = 1, CreatedAt = now, UpdatedAt = now };

            await AuditAsync(tenantId, actor, "person_contact_point.set", personId, "contact_point_set", correlationId, cancellationToken);
            return await repository.CreateAsync(created, CancellationToken.None);
        }

        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
            ThrowIfInvalid(errors);
            expectedVersion = default;
        }

        if (expectedVersion != current.Version)
        {
            throw DuplicateCandidateError.ContactPointConcurrencyConflict(personId, expectedVersion, current.Version);
        }

        var updated = current with { Email = email, Phone = phone, Version = current.Version + 1, UpdatedAt = UtcInstant.Now() };

        await AuditAsync(tenantId, actor, "person_contact_point.set", personId, "contact_point_set", correlationId, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public Task<PersonContactPoint?> GetAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.GetAsync(tenantId, personId, cancellationToken);
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, string reasonCode, string correlationId, CancellationToken cancellationToken)
    {
        var target = new AuditTarget("person_contact_point", personId);
        var detail = new Dictionary<string, (object?, string)> { ["person_id"] = (personId, "internal") };
        var auditEvent = DuplicateCandidateAuditEventBuilder.Build(
            tenantId, actor, action, target, AuditOutcome.Allowed, reasonCode, correlationId, causationId: null, detail, withheld: ContactFieldNames);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return; // confirmed: the write completed without throwing
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw DuplicateCandidateError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw DuplicateCandidateError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as success.
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw DuplicateCandidateError.TenantContextMissing();

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw DuplicateCandidateError.Validation(errors);
        }
    }

    private static string? ValidateEmail(string? value, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > 254)
        {
            errors.Add(new ValidationErrorItem("email", "out_of_range", "email must be at most 254 characters"));
            return null;
        }

        if (ControlCharacterPattern().IsMatch(trimmed) || !EmailPattern().IsMatch(trimmed))
        {
            errors.Add(new ValidationErrorItem("email", "invalid_format", "email must be a well-formed address"));
            return null;
        }

        return trimmed.ToLowerInvariant();
    }

    private static string? ValidatePhone(string? value, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Length > 32)
        {
            errors.Add(new ValidationErrorItem("phone", "out_of_range", "phone must be at most 32 characters"));
            return null;
        }

        if (ControlCharacterPattern().IsMatch(trimmed))
        {
            errors.Add(new ValidationErrorItem("phone", "invalid_format", "phone must not contain control characters"));
            return null;
        }

        // Comparison normalizes further (digits only, see DuplicateReviewService) - stored
        // exactly as entered (trimmed) so a display/correction round-trip is lossless.
        return trimmed;
    }
}
