using System.Text.RegularExpressions;
using Biolog.Features.PersonRegistry.Api;
using Biolog.Features.PersonRegistry.Audit;
using Biolog.Features.PersonRegistry.Domain;
using Biolog.Features.PersonRegistry.Identifiers;
using Biolog.Features.PersonRegistry.Tenancy;

namespace Biolog.Features.PersonRegistry;

/// <summary>
/// Application service for BIO-IDN-001. Implements ADR-0003 Decision 7's audit-atomicity
/// pattern - established across BIO-TEN-001 (three rounds), confirmed on BIO-TEN-002 and
/// BIO-DEV-001 - applied fresh here: every mutating method holds an in-process, per-person-id
/// lock (<see cref="PersonLockRegistry"/>) across the whole read-validate-audit-commit
/// sequence, audits BEFORE committing, resolves an ambiguous audit-write throw via
/// <see cref="IPersonAuditSink.ExistsAsync"/> rather than assuming failure, and passes
/// <see cref="CancellationToken.None"/> to the repository commit once the audit write is
/// confirmed durable - a caller cancellation arriving after that point must not abort a write
/// whose audit event already durably claims it happened. See <see cref="AuditAsync"/>.
///
/// <b>Registration lifetime and locking.</b> Unlike <c>TenantService</c>/<c>TenantDomainService</c>
/// (Singleton, owning their own lock table directly), this service is registered Scoped -
/// see <see cref="PersonLockRegistry"/>'s header for why - and receives its shared lock table
/// through DI instead of owning it.
///
/// <b>Tenant context.</b> Every method reads <see cref="ICurrentTenantProvider.TenantId"/>
/// itself (<see cref="RequireTenant"/>), never accepts one from a caller, and throws
/// <see cref="PersonError.TenantContextMissing"/> when none is resolved - BIO-IDN-001-AC02,
/// the same fail-closed rule <c>DeviceService</c> (BIO-DEV-001) applies against the real
/// <c>ITenantContextAccessor</c> it can reach directly. This feature reaches an equivalent
/// value through its own <see cref="ICurrentTenantProvider"/> instead (see that interface's
/// header for why) - whatever registers this feature into the packaged host is responsible
/// for providing an implementation backed by the real resolved context.
///
/// <b>Audit trail privacy.</b> PERSON carries personal data (name, date of birth -
/// <c>contracts/schemas/data-classification.json</c>: both <c>personal</c>, rank 2,
/// <c>allowlist_only</c>). This service never places an actual PII VALUE into an audit
/// event's <c>Detail</c> - every mutation instead lists every PII field name it touches in
/// <see cref="PersonPiiFieldNames"/> as the event's <c>Withheld</c> set, which
/// <c>audit-event.schema.json</c> describes exactly for this purpose ("Field NAMES
/// deliberately not carried. Never values."). This is a stricter posture than the schema's
/// own <c>allowlist_only</c> envelope for <c>personal</c> data technically permits - a
/// deliberate choice: an audit trail that proves WHO changed WHAT KIND of thing, on WHICH
/// record, WHEN, does not need to also become a second copy of a person's name to do its job.
/// <c>Detail</c> therefore only ever carries non-PII lifecycle metadata (a status word), which
/// is <c>internal</c>, not <c>personal</c> - see each method below.
/// </summary>
internal sealed partial class PersonService(
    ICurrentTenantProvider tenantProvider, IPersonRepository repository, IPersonAuditSink auditSink, PersonLockRegistry lockRegistry)
{
    private static readonly IReadOnlyList<string> PersonPiiFieldNames =
        ["title", "first_name", "middle_name", "last_name", "preferred_name", "born_on"];

    // ADR-0001 §2.2 / contracts/schemas/calendar-date.schema.json, ported verbatim: year
    // 1000-9999, proleptic Gregorian leap rule, no naive/guessed dates.
    [GeneratedRegex(@"^(?:[1-9]\d{3}-(?:0[13578]|1[02])-(?:0[1-9]|[12]\d|3[01])|[1-9]\d{3}-(?:0[469]|11)-(?:0[1-9]|[12]\d|30)|[1-9]\d{3}-02-(?:0[1-9]|1\d|2[0-8])|(?:[1-9]\d(?:0[48]|[2468][048]|[13579][26])|(?:[2468][048]|[13579][26])00)-02-29)$")]
    private static partial Regex CalendarDatePattern();

    public async Task<Person> RegisterAsync(
        RegisterPersonRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var errors = new List<ValidationErrorItem>();
        var title = ValidateOptionalNamePart(request.Title, "title", errors);
        var firstName = ValidateRequiredNamePart(request.FirstName, "first_name", errors);
        var middleName = ValidateOptionalNamePart(request.MiddleName, "middle_name", errors);
        var lastName = ValidateRequiredNamePart(request.LastName, "last_name", errors);
        var preferredName = ValidateOptionalNamePart(request.PreferredName, "preferred_name", errors);
        var bornOn = ValidateOptionalBornOn(request.BornOn, errors);
        ThrowIfInvalid(errors);

        var now = UtcInstant.Now();
        var person = new Person(CanonicalId.NewPersonId(), tenantId)
        {
            Title = title,
            FirstName = firstName!,
            MiddleName = middleName,
            LastName = lastName!,
            PreferredName = preferredName,
            BornOn = bornOn,
            Status = PersonStatus.PreEnrolled,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // A freshly minted UUIDv7 cannot already be locked by another in-flight mutation,
        // but taking the lock here anyway keeps create on the identical audit-then-commit
        // discipline as every other mutation (see TenantService.CreateAsync's identical
        // reasoning).
        using var _ = await lockRegistry.AcquireAsync(person.Id, cancellationToken);

        await AuditAsync(tenantId, actor, "person.register", person.Id, "person_registered", correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)> { ["status"] = (person.Status.ToWireString(), "internal") },
            cancellationToken);

        // Once the audit write is confirmed, the audit event is the real point of no
        // return: a cancellation signal arriving in the gap before the commit must not be
        // allowed to abort it, or the durable Allowed event would outlive a change that
        // never touched the store.
        return await repository.CreateAsync(person, CancellationToken.None);
    }

    public Task<Person?> GetAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.GetAsync(tenantId, personId, cancellationToken);
    }

    public Task<IReadOnlyList<Person>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.ListByTenantAsync(tenantId, cancellationToken);
    }

    /// <summary>
    /// BIO-IDN-001-AC03 (lifecycle transitions) and AC04 (governed deletion: transitioning to
    /// <see cref="PersonStatus.Archived"/> IS the deletion operation - there is no other one).
    /// </summary>
    public async Task<Person> UpdateStatusAsync(
        string personId, UpdatePersonStatusRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(personId, cancellationToken);
        var current = await repository.GetAsync(tenantId, personId, cancellationToken) ?? throw PersonError.NotFound(personId);

        var errors = new List<ValidationErrorItem>();
        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
            expectedVersion = default;
        }

        if (!PersonStatusExtensions.TryParse(request.Status, out var newStatus))
        {
            errors.Add(new ValidationErrorItem(
                "status", "invalid_format",
                "status must be one of PRE_ENROLLED, VERIFIED, ACTIVE, SUSPENDED, EXPIRED, FORMER_ALUMNI, REVOKED, ARCHIVED"));
        }

        ThrowIfInvalid(errors);

        if (expectedVersion != current.Version)
        {
            throw PersonError.ConcurrencyConflict(personId, expectedVersion, current.Version);
        }

        if (newStatus == current.Status)
        {
            return current; // no-op transition: nothing changed, nothing to audit
        }

        if (!PersonLifecycleTransitions.IsAllowed(current.Status, newStatus))
        {
            throw PersonError.InvalidStatusTransition(current.Status, newStatus);
        }

        var updated = current with { Status = newStatus, Version = current.Version + 1, UpdatedAt = UtcInstant.Now() };

        await AuditAsync(tenantId, actor, "person.status_change", personId, "person_status_changed", correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)>
            {
                ["from_status"] = (current.Status.ToWireString(), "internal"),
                ["to_status"] = (newStatus.ToWireString(), "internal"),
            },
            cancellationToken);

        // Guaranteed to succeed: the per-person lock held since entry means no other writer
        // in this process could have changed `current` since it was read. CancellationToken
        // is None: the audit event above is already confirmed durable (see class header).
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    /// <summary>
    /// Durably records <paramref name="detail"/> as a <see cref="PersonAuditEvent"/>,
    /// resolving an ambiguous <see cref="IPersonAuditSink.WriteAsync"/> throw via
    /// <see cref="IPersonAuditSink.ExistsAsync"/> before treating it as a real failure
    /// (ADR-0003 Decision 7, element 4). Every PII field name is withheld unconditionally
    /// (see class header) - callers never need to, and cannot, opt a PII value into this
    /// event's Detail.
    /// </summary>
    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, string reasonCode, string correlationId, string? causationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = PersonAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("person", personId), AuditOutcome.Allowed, reasonCode, correlationId, causationId,
            detail, withheld: PersonPiiFieldNames);

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
            throw PersonError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw PersonError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as a
        // success, so the caller's subsequent commit makes this event's claim true.
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw PersonError.TenantContextMissing();

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw PersonError.Validation(errors);
        }
    }

    private static string? ValidateRequiredNamePart(string? value, string field, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem(field, "required", $"{field} is required"));
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

    private static string? ValidateOptionalNamePart(string? value, string field, List<ValidationErrorItem> errors)
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

    private static string? ValidateOptionalBornOn(string? value, List<ValidationErrorItem> errors)
    {
        if (value is null)
        {
            return null;
        }

        if (!CalendarDatePattern().IsMatch(value))
        {
            errors.Add(new ValidationErrorItem(
                "born_on", "invalid_format", "born_on must be a real YYYY-MM-DD calendar date between year 1000 and 9999 (ADR-0001 §2.2)"));
            return null;
        }

        return value;
    }
}
