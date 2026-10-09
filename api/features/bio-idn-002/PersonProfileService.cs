using System.Text.RegularExpressions;
using Biolog.Features.PersonProfileRegistry.Api;
using Biolog.Features.PersonProfileRegistry.Audit;
using Biolog.Features.PersonProfileRegistry.Domain;
using Biolog.Features.PersonProfileRegistry.Identifiers;
using Biolog.Features.PersonProfileRegistry.Tenancy;

namespace Biolog.Features.PersonProfileRegistry;

/// <summary>
/// Application service for BIO-IDN-002. Implements ADR-0003 Decision 7's audit-atomicity
/// pattern exactly as established across BIO-TEN-001/BIO-TEN-002/BIO-DEV-001/BIO-IDN-001:
/// every mutating method holds an in-process, per-<c>(PersonId, Type)</c> lock
/// (<see cref="PersonProfileLockRegistry"/>) across the whole read-validate-audit-commit
/// sequence, audits BEFORE committing, resolves an ambiguous audit-write throw via
/// <see cref="IPersonProfileAuditSink.ExistsAsync"/> rather than assuming failure, and passes
/// <see cref="CancellationToken.None"/> to the repository commit once the audit write is
/// confirmed durable.
///
/// Registered Scoped, like BIO-IDN-001's <c>PersonService</c> and for the identical reason:
/// this service reads a per-request <see cref="ICurrentTenantProvider"/>, so it cannot be
/// Singleton (captive-dependency defect) - the lock table lives in its own Singleton class,
/// <see cref="PersonProfileLockRegistry"/>, injected here rather than owned directly.
///
/// <b>AC04 - never creates a PERSON.</b> Every method takes an existing <paramref
/// name="personId"/>; adding a second role to an existing person is exactly
/// <see cref="AssignAsync"/>: attach a second profile, never construct a second person.
///
/// <b>BIO-IDN-002-QA-01 (Seyi round 1).</b> <see cref="AssignAsync"/> validates
/// <paramref name="personId"/>'s SHAPE itself (a well-formed <c>per_</c> identifier) but its
/// EXISTENCE in the resolved tenant is proven through <see cref="IPersonExistenceGateway"/> -
/// this project's own narrow, read-only seam onto BIO-IDN-001's real store (see that
/// interface's header for why a project reference was still not needed to close this). A
/// person that does not exist, or exists only in a different tenant, is rejected identically
/// as <c>person_not_found</c> before the per-(person, type) lock is even acquired, and before
/// any audit event is written - an operation with no real target is not itself audit-worthy,
/// the same posture <see cref="AssignAsync"/>'s duplicate-profile-type check already takes.
/// </summary>
internal sealed partial class PersonProfileService(
    ICurrentTenantProvider tenantProvider,
    IPersonProfileRepository repository,
    IPersonProfileAuditSink auditSink,
    PersonProfileLockRegistry lockRegistry,
    IPersonExistenceGateway personGateway)
{
    // ADR-0001 §2.1 / contracts/schemas/utc-instant.schema.json, ported verbatim.
    [GeneratedRegex(@"^(?:[1-9]\d{3}-(?:0[13578]|1[02])-(?:0[1-9]|[12]\d|3[01])|[1-9]\d{3}-(?:0[469]|11)-(?:0[1-9]|[12]\d|30)|[1-9]\d{3}-02-(?:0[1-9]|1\d|2[0-8])|(?:[1-9]\d(?:0[48]|[2468][048]|[13579][26])|(?:[2468][048]|[13579][26])00)-02-29)T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d\.\d{3}Z$")]
    private static partial Regex UtcInstantPattern();

    public async Task<PersonProfile> AssignAsync(
        string personId, AssignProfileRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var errors = new List<ValidationErrorItem>();
        if (!CanonicalId.IsPersonId(personId))
        {
            errors.Add(new ValidationErrorItem("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier"));
        }

        var type = ValidateType(request.Type, errors);
        var customLabel = ValidateCustomLabel(request.CustomLabel, type, errors);
        var organisationId = ValidateOptionalHierarchyId(request.OrganisationId, "organisation_id", errors);
        var campusId = ValidateOptionalHierarchyId(request.CampusId, "campus_id", errors);
        var branchId = ValidateOptionalHierarchyId(request.BranchId, "branch_id", errors);
        var expiresAt = ValidateOptionalInstant(request.ExpiresAt, "expires_at", errors);
        ThrowIfInvalid(errors);

        // BIO-IDN-002-QA-01: prove the PERSON exists in the resolved tenant before anything
        // else - not just a shape check. Deliberately outside the lock/duplicate-check below:
        // a read-only existence check does not depend on, and is not protected by, this
        // profile's own per-(person, type) lock.
        if (!await personGateway.ExistsAsync(personId, cancellationToken))
        {
            throw PersonProfileError.PersonNotFound(personId);
        }

        using var _ = await lockRegistry.AcquireAsync(personId, type, cancellationToken);

        if (await repository.GetAsync(tenantId, personId, type, cancellationToken) is not null)
        {
            // Pre-check under the lock, before audit: an operation that never had a real
            // target (a genuine duplicate attempt) is not itself audit-worthy - the same
            // posture BIO-TEN-002's duplicate-hostname check takes.
            throw PersonProfileError.DuplicateProfileType(personId, type);
        }

        var now = UtcInstant.Now();
        var profile = new PersonProfile(personId, tenantId, type, customLabel)
        {
            OrganisationId = organisationId,
            CampusId = campusId,
            BranchId = branchId,
            Status = ProfileStatus.Active,
            ActivatedAt = now,
            ExpiresAt = expiresAt,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "person_profile.assign", personId, type, "profile_assigned", correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)> { ["type"] = (type.ToWireString(), "internal") },
            cancellationToken);

        // Once the audit write is confirmed, only a genuine fault in the repository call may
        // stop the commit - not a mere cancellation signal (see class header).
        return await repository.CreateAsync(profile, CancellationToken.None);
    }

    public Task<PersonProfile?> GetAsync(string personId, ProfileType type, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.GetAsync(tenantId, personId, type, cancellationToken);
    }

    public Task<IReadOnlyList<PersonProfile>> ListAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.ListByPersonAsync(tenantId, personId, cancellationToken);
    }

    /// <summary>BIO-IDN-002's only governed transition: Active -&gt; Revoked. The profile's "governed deletion" - the record and its operational history are never removed.</summary>
    public async Task<PersonProfile> RevokeAsync(
        string personId, ProfileType type, RevokeProfileRequest request, AuditActor actor, string correlationId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(personId, type, cancellationToken);
        var current = await repository.GetAsync(tenantId, personId, type, cancellationToken) ?? throw PersonProfileError.NotFound(personId, type);

        var errors = new List<ValidationErrorItem>();
        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
            expectedVersion = default;
        }

        ThrowIfInvalid(errors);

        if (expectedVersion != current.Version)
        {
            throw PersonProfileError.ConcurrencyConflict(personId, type, expectedVersion, current.Version);
        }

        if (current.Status == ProfileStatus.Revoked)
        {
            return current; // no-op: already revoked, nothing to audit
        }

        var updated = current with { Status = ProfileStatus.Revoked, Version = current.Version + 1, UpdatedAt = UtcInstant.Now() };

        await AuditAsync(tenantId, actor, "person_profile.revoke", personId, type, "profile_revoked", correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)>(),
            cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, ProfileType type, string reasonCode, string correlationId,
        string? causationId, IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var target = new AuditTarget("person_profile", $"{personId}/{type.ToWireString()}");
        var auditEvent = PersonProfileAuditEventBuilder.Build(
            tenantId, actor, action, target, AuditOutcome.Allowed, reasonCode, correlationId, causationId, detail);

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
            throw PersonProfileError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw PersonProfileError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as success.
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw PersonProfileError.TenantContextMissing();

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw PersonProfileError.Validation(errors);
        }
    }

    private static ProfileType ValidateType(string? value, List<ValidationErrorItem> errors)
    {
        if (!ProfileTypeExtensions.TryParse(value, out var type))
        {
            errors.Add(new ValidationErrorItem(
                "type", "invalid_format", "type must be one of STUDENT, EMPLOYEE, VISITOR, CONTRACTOR, ALUMNI, PARENT, TENANT_DEFINED"));
        }

        return type;
    }

    private static string? ValidateCustomLabel(string? value, ProfileType type, List<ValidationErrorItem> errors)
    {
        if (type == ProfileType.TenantDefined)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new ValidationErrorItem("custom_label", "required", "custom_label is required when type is TENANT_DEFINED"));
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.Length > 100)
            {
                errors.Add(new ValidationErrorItem("custom_label", "out_of_range", "custom_label must be at most 100 characters"));
                return null;
            }

            return trimmed;
        }

        if (value is not null)
        {
            errors.Add(new ValidationErrorItem("custom_label", "invalid_format", "custom_label must be omitted unless type is TENANT_DEFINED"));
        }

        return null;
    }

    private static string? ValidateOptionalHierarchyId(string? value, string field, List<ValidationErrorItem> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem(field, "invalid_format", $"{field} must not be blank when present"));
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 200)
        {
            errors.Add(new ValidationErrorItem(field, "out_of_range", $"{field} must be at most 200 characters"));
            return null;
        }

        return trimmed;
    }

    private static string? ValidateOptionalInstant(string? value, string field, List<ValidationErrorItem> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (!UtcInstantPattern().IsMatch(value))
        {
            errors.Add(new ValidationErrorItem(field, "invalid_format", $"{field} must be an RFC 3339 UTC instant (ADR-0001 §2.1)"));
            return null;
        }

        return value;
    }
}
