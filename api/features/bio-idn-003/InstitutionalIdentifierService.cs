using System.Text.RegularExpressions;
using Biolog.Features.InstitutionalIdentifiers.Api;
using Biolog.Features.InstitutionalIdentifiers.Audit;
using Biolog.Features.InstitutionalIdentifiers.Domain;
using Biolog.Features.InstitutionalIdentifiers.Identifiers;
using Biolog.Features.InstitutionalIdentifiers.Tenancy;

namespace Biolog.Features.InstitutionalIdentifiers;

/// <summary>
/// Application service for BIO-IDN-003. Implements ADR-0003 Decision 7's audit-atomicity
/// pattern exactly as established across BIO-TEN-001/BIO-TEN-002/BIO-DEV-001/BIO-IDN-001/BIO-IDN-002:
/// every mutating method holds an in-process, per-<c>(PersonId, Type, CustomLabel)</c> lock
/// (<see cref="InstitutionalIdentifierLockRegistry"/>) across the whole
/// read-validate-audit-commit sequence, audits BEFORE committing, resolves an ambiguous
/// audit-write throw via <see cref="IInstitutionalIdentifierAuditSink.ExistsAsync"/> rather than
/// assuming failure, and passes <see cref="CancellationToken.None"/> to the repository commit
/// once the audit write is confirmed durable.
///
/// Registered Scoped, like BIO-IDN-001's <c>PersonService</c> and BIO-IDN-002's
/// <c>PersonProfileService</c>, for the identical reason: this service reads a per-request
/// <see cref="ICurrentTenantProvider"/>, so it cannot be Singleton - the lock table lives in its
/// own Singleton class, <see cref="InstitutionalIdentifierLockRegistry"/>.
///
/// <b>AC01 - typed and tenant-scoped.</b> Every method reads <see cref="RequireTenant"/>
/// itself; <see cref="AssignAsync"/>'s <paramref name="request"/> carries an
/// <see cref="IdentifierType"/>.
///
/// <b>AC02 - uniqueness rules are configurable per type.</b> <see cref="AssignAsync"/> resolves
/// <see cref="IdentifierUniquenessRule"/> for <c>(Type, CustomLabel)</c> before committing - see
/// that type's header for the fail-closed default.
///
/// <b>BIO-IDN-003-QA-01 (Seyi round 1, High).</b> AC02's uniqueness invariant is scoped to
/// <c>(TenantId, Type, CustomLabel)</c> ACROSS EVERY PERSON in the tenant, but the per-person
/// lock above only ever serializes two callers targeting the SAME person - it does nothing for
/// two callers assigning the SAME value to two DIFFERENT people. Before this fix, both could
/// read <see cref="IInstitutionalIdentifierRepository.ValueExistsAsync"/> as <c>false</c>
/// concurrently and both commit, admitting the same enforced-unique value twice with two
/// ALLOWED audits - the exact two-lock-scope gap BIO-TEN-002's <c>TenantDomainService</c>
/// already solved once for a structurally identical problem (a per-hostname lock protects a
/// single record's own mutation; a global collision invariant needs a lock keyed by the SCOPE
/// the invariant spans, not by the narrower per-record key). <see cref="AssignAsync"/> now
/// acquires <see cref="IdentifierUniquenessRuleLockRegistry"/>'s <c>(TenantId, Type,
/// CustomLabel)</c> lock FIRST (the broader, outer lock - the same scope
/// <see cref="IdentifierUniquenessRuleService.SetRuleAsync"/> already used, so a rule change is
/// now also serialized against any in-flight assignment for that type, closing the review's
/// second observation) and the narrower per-person lock second, holding both across the
/// history-read, the uniqueness check and the commit. <see cref="RetireAsync"/> does not touch
/// cross-person uniqueness (a retired value stays reserved forever when enforced - see
/// <see cref="InMemoryInstitutionalIdentifierRepository.ValueExistsAsync"/>), so it keeps only
/// the narrower per-person lock.
///
/// <b>AC03 - history/status is preserved.</b> <see cref="RetireAsync"/> is BIO-IDN-003's only
/// governed transition (Active -&gt; Retired, terminal, idempotent no-op if already Retired).
/// Nothing is ever deleted or edited in place - see <see cref="InstitutionalIdentifier"/>'s
/// header for why a "correction" is a new record at the next
/// <see cref="InstitutionalIdentifier.SequenceNumber"/>, not a mutation of
/// <see cref="InstitutionalIdentifier.Value"/>.
///
/// <b>Applying BIO-IDN-002-QA-01 from the start (not after a review round).</b>
/// <see cref="AssignAsync"/> proves <paramref name="personId"/>'s EXISTENCE in the resolved
/// tenant through <see cref="IPersonExistenceGateway"/> - this project's own narrow, read-only
/// seam onto BIO-IDN-001's real store - before the per-(person, type, custom_label) lock is
/// even acquired, and before any audit event is written. A person that does not exist, or
/// exists only in a different tenant, is rejected identically as <c>person_not_found</c> - an
/// operation with no real target is not itself audit-worthy, the same posture the
/// already-active and duplicate-value checks below take.
///
/// <b>Audit trail privacy.</b> <c>contracts/schemas/data-classification.json</c> names
/// "institutional identifier" as a worked example of <c>personal</c> data (rank 2), alongside
/// name and date of birth. This service never places the actual identifier
/// <see cref="InstitutionalIdentifier.Value"/> into an audit event's <c>Detail</c> - every
/// mutation instead lists <c>"value"</c> in the event's <c>Withheld</c> set, mirroring
/// BIO-IDN-001's <c>PersonService</c> exactly. <c>Detail</c> only ever carries non-PII
/// lifecycle metadata (type, custom_label, sequence_number), which is <c>internal</c>, not
/// <c>personal</c>.
/// </summary>
internal sealed partial class InstitutionalIdentifierService(
    ICurrentTenantProvider tenantProvider,
    IInstitutionalIdentifierRepository repository,
    IIdentifierUniquenessRuleRepository ruleRepository,
    IInstitutionalIdentifierAuditSink auditSink,
    InstitutionalIdentifierLockRegistry lockRegistry,
    IdentifierUniquenessRuleLockRegistry uniquenessLockRegistry,
    IPersonExistenceGateway personGateway)
{
    private static readonly IReadOnlyList<string> ValueFieldNames = ["value"];

    public async Task<InstitutionalIdentifier> AssignAsync(
        string personId, AssignIdentifierRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var errors = new List<ValidationErrorItem>();
        if (!CanonicalId.IsPersonId(personId))
        {
            errors.Add(new ValidationErrorItem("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier"));
        }

        var type = ValidateType(request.Type, errors);
        var customLabel = ValidateCustomLabel(request.CustomLabel, type, errors);
        var value = ValidateValue(request.Value, errors);
        ThrowIfInvalid(errors);

        // Applying BIO-IDN-002-QA-01 from the start: prove the PERSON exists in the resolved
        // tenant before anything else. Deliberately outside the lock/uniqueness checks below -
        // a read-only existence check does not depend on, and is not protected by, this
        // identifier's own per-(person, type, custom_label) lock.
        if (!await personGateway.ExistsAsync(personId, cancellationToken))
        {
            throw InstitutionalIdentifierError.PersonNotFound(personId);
        }

        // BIO-IDN-003-QA-01: the outer lock is keyed by (TenantId, Type, CustomLabel) - the
        // SAME scope AC02's uniqueness invariant spans, and the SAME scope
        // IdentifierUniquenessRuleService.SetRuleAsync locks - so two assignments of the same
        // value to two DIFFERENT people, and a rule change racing an assignment, are both fully
        // serialized. The inner, per-person lock is still acquired second, inside it, so the
        // "already active" check and the whole commit remain covered exactly as before.
        using var __ = await uniquenessLockRegistry.AcquireAsync(tenantId, type, customLabel, cancellationToken);
        using var _ = await lockRegistry.AcquireAsync(personId, type, customLabel, cancellationToken);

        var history = await repository.ListHistoryAsync(tenantId, personId, type, customLabel, cancellationToken);
        if (history.Any(h => h.Status == IdentifierStatus.Active))
        {
            // AC03's structural invariant: at most one current identifier per (person, type,
            // custom_label) - a real target already exists, and this operation never had a
            // legitimate one, so it is not itself audit-worthy, mirroring BIO-IDN-002's
            // duplicate-profile-type posture.
            throw InstitutionalIdentifierError.IdentifierAlreadyActive(personId, type, customLabel);
        }

        var rule = await ruleRepository.GetAsync(tenantId, type, customLabel, cancellationToken);
        var enforced = rule?.Enforced ?? true; // fail-closed default - see IdentifierUniquenessRule's header
        if (enforced && await repository.ValueExistsAsync(tenantId, type, customLabel, value, cancellationToken))
        {
            throw InstitutionalIdentifierError.DuplicateIdentifierValue(tenantId, type, customLabel);
        }

        var now = UtcInstant.Now();
        var sequenceNumber = history.Count + 1;
        var identifier = new InstitutionalIdentifier(personId, tenantId, type, customLabel, sequenceNumber, value)
        {
            Status = IdentifierStatus.Active,
            RetiredAt = null,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "institutional_identifier.assign", personId, type, customLabel, sequenceNumber, "identifier_assigned", correlationId,
            causationId: null, cancellationToken);

        // Once the audit write is confirmed, only a genuine fault in the repository call may
        // stop the commit - not a mere cancellation signal (see class header).
        return await repository.CreateAsync(identifier, CancellationToken.None);
    }

    public Task<InstitutionalIdentifier?> GetActiveAsync(string personId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.GetActiveAsync(tenantId, personId, type, customLabel, cancellationToken);
    }

    /// <summary>AC03: full history for one (type, custom_label), including every retired record - never just the active one.</summary>
    public Task<IReadOnlyList<InstitutionalIdentifier>> ListHistoryAsync(
        string personId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.ListHistoryAsync(tenantId, personId, type, customLabel, cancellationToken);
    }

    public Task<IReadOnlyList<InstitutionalIdentifier>> ListAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.ListByPersonAsync(tenantId, personId, cancellationToken);
    }

    /// <summary>BIO-IDN-003's only governed transition: Active -&gt; Retired. The record and its operational history are never removed - AC03.</summary>
    public async Task<InstitutionalIdentifier> RetireAsync(
        string personId, IdentifierType type, string? customLabel, int sequenceNumber, RetireIdentifierRequest request, AuditActor actor,
        string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(personId, type, customLabel, cancellationToken);
        var current = await repository.GetAsync(tenantId, personId, type, customLabel, sequenceNumber, cancellationToken)
            ?? throw InstitutionalIdentifierError.IdentifierNotFound(personId, type, customLabel, sequenceNumber);

        var errors = new List<ValidationErrorItem>();
        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
            expectedVersion = default;
        }

        ThrowIfInvalid(errors);

        if (expectedVersion != current.Version)
        {
            throw InstitutionalIdentifierError.ConcurrencyConflict(personId, type, sequenceNumber, expectedVersion, current.Version);
        }

        if (current.Status == IdentifierStatus.Retired)
        {
            return current; // no-op: already retired, nothing to audit
        }

        var updated = current with { Status = IdentifierStatus.Retired, RetiredAt = UtcInstant.Now(), Version = current.Version + 1, UpdatedAt = UtcInstant.Now() };

        await AuditAsync(tenantId, actor, "institutional_identifier.retire", personId, type, customLabel, sequenceNumber, "identifier_retired", correlationId,
            causationId: null, cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, IdentifierType type, string? customLabel, int sequenceNumber,
        string reasonCode, string correlationId, string? causationId, CancellationToken cancellationToken)
    {
        var target = new AuditTarget("institutional_identifier", $"{personId}/{type.ToWireString()}/{sequenceNumber}");
        var detail = new Dictionary<string, (object?, string)>
        {
            ["type"] = (type.ToWireString(), "internal"),
            ["custom_label"] = (customLabel, "internal"),
            ["sequence_number"] = (sequenceNumber, "internal"),
        };
        var auditEvent = InstitutionalIdentifierAuditEventBuilder.Build(
            tenantId, actor, action, target, AuditOutcome.Allowed, reasonCode, correlationId, causationId, detail, withheld: ValueFieldNames);

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
            throw InstitutionalIdentifierError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw InstitutionalIdentifierError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as success.
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw InstitutionalIdentifierError.TenantContextMissing();

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw InstitutionalIdentifierError.Validation(errors);
        }
    }

    private static IdentifierType ValidateType(string? value, List<ValidationErrorItem> errors)
    {
        if (!IdentifierTypeExtensions.TryParse(value, out var type))
        {
            errors.Add(new ValidationErrorItem("type", "invalid_format", "type must be one of MATRICULATION_NUMBER, STAFF_NUMBER, TENANT_DEFINED"));
        }

        return type;
    }

    private static string? ValidateCustomLabel(string? value, IdentifierType type, List<ValidationErrorItem> errors)
    {
        if (type == IdentifierType.TenantDefined)
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

    [GeneratedRegex(@"[\x00-\x1F\x7F]")]
    private static partial Regex ControlCharacterPattern();

    private static string ValidateValue(string? value, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem("value", "required", "value is required"));
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 64)
        {
            errors.Add(new ValidationErrorItem("value", "out_of_range", "value must be at most 64 characters"));
            return string.Empty;
        }

        if (ControlCharacterPattern().IsMatch(trimmed))
        {
            errors.Add(new ValidationErrorItem("value", "invalid_format", "value must not contain control characters"));
            return string.Empty;
        }

        return trimmed;
    }
}
