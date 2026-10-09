using Biolog.Features.CardIssuance.Audit;
using Biolog.Features.CardIssuance.Domain;
using Biolog.Features.CardIssuance.Identifiers;
using Biolog.Features.CardIssuance.Tenancy;

namespace Biolog.Features.CardIssuance;

/// <summary>
/// Application service for BIO-CRD-005. Implements ADR-0003 Decision 7's audit-atomicity
/// pattern - established across BIO-TEN-001/002, BIO-DEV-001 and every BIO-IDN story - applied
/// fresh here: every mutating method holds an in-process, per-(tenant, person, card class) lock
/// (<see cref="CardIssueLockRegistry"/>) across its own read-validate-audit-commit sequence,
/// audits BEFORE committing, resolves an ambiguous audit-write throw via
/// <see cref="ICardIssueAuditSink.ExistsAsync"/> rather than assuming failure, and passes
/// <see cref="CancellationToken.None"/> to the repository commit once the audit write is
/// confirmed durable.
///
/// <b>AC04's transactional guarantee, concretely.</b> <see cref="ActivateAsync"/> reads the
/// current active sibling (if any) and writes the new Active status inside the SAME
/// per-(tenant, person, card class) lock acquisition that serialises every other mutation
/// against that key - two concurrent activation attempts for the same person/card class
/// cannot both observe "no active sibling" and both proceed, because the second waits for the
/// first to release the lock and then re-reads a state that already shows the first's card as
/// Active. This is what makes "prevents two active physical cards" an actual guarantee, not
/// merely a check that could still race.
///
/// <b>Tenant context.</b> Every method reads <see cref="ICurrentTenantProvider.TenantId"/>
/// itself, never accepts one from a caller, and throws
/// <see cref="CardIssueError.TenantContextMissing"/> when none is resolved - the same
/// fail-closed rule every sibling tenant-plane story applies.
///
/// <b>Actor identity (ADR-0006 Decision 1/4).</b> Every mutating method's caller supplies an
/// <see cref="AuditActor"/> purely for audit ATTRIBUTION and for <see cref="CardIssueProvenance.Operator"/>
/// - nothing about who may issue/encode/print/verify/activate/deactivate a card is gated on the
/// asserted identity. This is Decision 1's safe "audit-attribution use" shape, the same
/// disclosed-residual category every sibling story in this codebase applies.
/// </summary>
internal sealed class CardIssueService(
    ICurrentTenantProvider tenantProvider,
    ICardIssueRepository repository,
    IPersonExistenceGateway personGateway,
    ICardIssueAuditSink auditSink,
    CardIssueLockRegistry lockRegistry,
    IIdempotencyStore idempotencyStore,
    TimeSpan? idempotencyWaitTimeout = null,
    TimeSpan? idempotencyWaitPollInterval = null)
{
    private const string ActivateOperation = "activate_card_issue";

    // TD-CRD-005-IDEMPOTENCY-RACE's own remediation: how long a claim-loser (a concurrent caller
    // whose Idempotency-Key is already owned by someone else's in-flight attempt) waits for that
    // owner's result before giving up with a 503 (ADR-0004/BIO-ARC-004's documented "claim-loser
    // waits bounded then idempotency_wait_timeout" contract). Both are injectable (constructor
    // parameters, defaulting to production-sized values) so a test can prove the timeout path
    // deterministically and fast, without actually waiting multiple real seconds.
    private readonly TimeSpan _idempotencyWaitTimeout = idempotencyWaitTimeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _idempotencyWaitPollInterval = idempotencyWaitPollInterval ?? TimeSpan.FromMilliseconds(20);
    public async Task<CardIssue> IssueAsync(
        string personId, string cardClass, IssueCardRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!CanonicalId.IsPersonId(personId))
        {
            throw CardIssueError.PersonNotFound(personId);
        }

        if (!await personGateway.ExistsAsync(personId, cancellationToken))
        {
            throw CardIssueError.PersonNotFound(personId);
        }

        var errors = new List<Api.ValidationErrorItem>();
        var normalisedClass = ValidateRequiredField(cardClass, "card_class", errors);
        var templateRef = ValidateRequiredField(request.TemplateRef, "template_ref", errors);
        var printerRef = ValidateRequiredField(request.PrinterRef, "printer_ref", errors);
        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, normalisedClass!, cancellationToken);

        var now = UtcInstant.Now();
        var provenance = new CardIssueProvenance
        {
            TemplateRef = templateRef!,
            PhotoRef = request.PhotoRef,
            LogoRef = request.LogoRef,
            SignatureRef = request.SignatureRef,
            Operator = actor,
            PrinterRef = printerRef!,
            IssuedAt = now,
        };

        // BIO-CRD-005-QA-01 (Chioma's acceptance review round 1): the sequence number is only
        // COMPUTED here, under the lock - nothing is committed yet. The row is built entirely
        // in memory, audited, and only written to the repository once that audit is confirmed
        // durable, the same lock -> validate -> audit -> commit order every other mutation in
        // this service already follows. The lock held since entry is what makes reading
        // NextSequenceNumber here and calling Create later, with no repository-level write in
        // between, race-free: no other issuance for this (tenant, person, card class) can run
        // concurrently and observe or claim the same sequence number.
        var sequenceNumber = repository.NextSequenceNumber(tenantId, personId, normalisedClass!);
        var issue = new CardIssue(tenantId, personId, normalisedClass!, sequenceNumber)
        {
            Status = CardIssueStatus.Issued,
            Version = 1,
            Provenance = provenance,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "card_issue.issue", personId, normalisedClass!, sequenceNumber, "card_issued", correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)>
            {
                ["card_class"] = (normalisedClass, "internal"),
                ["sequence_number"] = (sequenceNumber, "internal"),
                ["template_ref"] = (templateRef, "internal"),
                ["printer_ref"] = (printerRef, "internal"),
            },
            cancellationToken);

        // Once the audit write is confirmed, the audit event is the real point of no return: a
        // cancellation signal arriving in the gap before the commit must not be allowed to
        // abort it, or the durable Allowed event would outlive a card issue that never existed.
        return repository.Create(issue);
    }

    public Task<CardIssue?> GetAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return Task.FromResult(repository.Get(tenantId, personId, cardClass, sequenceNumber));
    }

    public Task<IReadOnlyList<CardIssue>> ListAsync(string personId, string cardClass, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return Task.FromResult(repository.List(tenantId, personId, cardClass));
    }

    /// <summary>BIO-CRD-005-AC03: encoding may happen at any status other than Deactivated, and only once per card issue.</summary>
    public Task<CardIssue> RecordEncodedAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        RecordStepAsync(personId, cardClass, sequenceNumber, expectedVersion, actor, correlationId,
            step: "encode", reasonCode: "card_encoded",
            alreadyRecorded: issue => issue.EncodedAt is not null,
            apply: (issue, now) => issue with { EncodedAt = now },
            cancellationToken);

    public Task<CardIssue> RecordPrintedAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        RecordStepAsync(personId, cardClass, sequenceNumber, expectedVersion, actor, correlationId,
            step: "print", reasonCode: "card_printed",
            alreadyRecorded: issue => issue.PrintedAt is not null,
            apply: (issue, now) => issue with { PrintedAt = now },
            cancellationToken);

    public Task<CardIssue> RecordVerifiedAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        RecordStepAsync(personId, cardClass, sequenceNumber, expectedVersion, actor, correlationId,
            step: "verify", reasonCode: "card_verified",
            alreadyRecorded: issue => issue.VerifiedAt is not null,
            apply: (issue, now) => issue with { VerifiedAt = now },
            cancellationToken);

    private async Task<CardIssue> RecordStepAsync(
        string personId, string cardClass, int sequenceNumber, int? expectedVersion, AuditActor actor, string correlationId,
        string step, string reasonCode, Func<CardIssue, bool> alreadyRecorded, Func<CardIssue, string, CardIssue> apply,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cardClass, cancellationToken);
        var current = repository.Get(tenantId, personId, cardClass, sequenceNumber) ?? throw CardIssueError.CardIssueNotFound(personId, cardClass, sequenceNumber);

        RequireExpectedVersion(expectedVersion, current.Version);

        if (current.Status == CardIssueStatus.Deactivated)
        {
            throw CardIssueError.InvalidTransition(step, current.Status);
        }

        if (alreadyRecorded(current))
        {
            throw CardIssueError.StepAlreadyRecorded(step);
        }

        var now = UtcInstant.Now();
        var updated = apply(current, now) with { Version = current.Version + 1, UpdatedAt = now };

        await AuditAsync(tenantId, actor, $"card_issue.{step}", personId, cardClass, sequenceNumber, reasonCode, correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)> { ["sequence_number"] = (sequenceNumber, "internal") },
            cancellationToken);

        return repository.Replace(current, updated);
    }

    /// <summary>
    /// BIO-CRD-005-AC04: refuses activation if a sibling for this (person, card class) is
    /// already Active - see class header for why the surrounding lock makes this a real
    /// transactional guarantee, not merely a check. ADR-0001 §3.4/ADR-0004 §4.3: card
    /// activation is a named required-idempotency-key operation - a retried activation must
    /// never mint a second activation; replaying the same key with the same request returns
    /// the original result, replaying with different content is rejected outright.
    /// </summary>
    public async Task<CardIssue> ActivateAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, string idempotencyKey, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw CardIssueError.IdempotencyKeyRequired();
        }

        // TD-CRD-005-IDEMPOTENCY-RACE: the original code checked only `claimed.State ==
        // Completed` - a shared InFlight record from a CONCURRENT caller looked identical to a
        // claim this caller itself just won, so a second caller fell through and activated a
        // second time regardless. `IdempotencyClaim.IsNewClaim` is the missing ownership signal:
        // only the true owner ever breaks out of this loop to proceed below. Every other caller
        // (someone else's claim is in flight) waits, bounded by `_idempotencyWaitTimeout`,
        // re-attempting its own `Claim` each iteration.
        var fingerprint = $"{personId}\u0000{cardClass}\u0000{sequenceNumber}\u0000{expectedVersion}";
        var deadline = DateTime.UtcNow + _idempotencyWaitTimeout;
        while (true)
        {
            var claim = idempotencyStore.Claim(tenantId, ActivateOperation, idempotencyKey, fingerprint);
            if (claim.Record.RequestFingerprint != fingerprint)
            {
                throw CardIssueError.IdempotencyKeyReuse();
            }

            if (claim.Record.State == IdempotencyRecordState.Completed)
            {
                return (CardIssue)claim.Record.Result!;
            }

            if (claim.IsNewClaim)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw CardIssueError.IdempotencyWaitTimeout(idempotencyKey);
            }

            await Task.Delay(_idempotencyWaitPollInterval, cancellationToken);
        }

        try
        {
            using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cardClass, cancellationToken);
            var current = repository.Get(tenantId, personId, cardClass, sequenceNumber) ?? throw CardIssueError.CardIssueNotFound(personId, cardClass, sequenceNumber);

            RequireExpectedVersion(expectedVersion, current.Version);

            if (current.Status != CardIssueStatus.Issued)
            {
                throw CardIssueError.InvalidTransition("activate", current.Status);
            }

            var active = repository.GetActive(tenantId, personId, cardClass);
            if (active is not null)
            {
                throw CardIssueError.AnotherCardAlreadyActive(personId, cardClass, active.SequenceNumber);
            }

            var now = UtcInstant.Now();
            var updated = current with { Status = CardIssueStatus.Active, ActivatedAt = now, Version = current.Version + 1, UpdatedAt = now };

            await AuditAsync(tenantId, actor, "card_issue.activate", personId, cardClass, sequenceNumber, "card_activated", correlationId, causationId: null,
                detail: new Dictionary<string, (object?, string)> { ["sequence_number"] = (sequenceNumber, "internal") },
                cancellationToken);

            var result = repository.Replace(current, updated);
            idempotencyStore.Complete(tenantId, ActivateOperation, idempotencyKey, result);
            return result;
        }
        catch
        {
            idempotencyStore.Release(tenantId, ActivateOperation, idempotencyKey);
            throw;
        }
    }

    /// <summary>Necessary counterpart to <see cref="ActivateAsync"/> - without an explicit way to retire the currently-active card, AC04's own invariant could never be exercised: a second card could never be activated at all. No AC names this operation directly, but AC04's "prevents two active physical cards" implies an operator has a real way to make room for the next one.</summary>
    public async Task<CardIssue> DeactivateAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cardClass, cancellationToken);
        var current = repository.Get(tenantId, personId, cardClass, sequenceNumber) ?? throw CardIssueError.CardIssueNotFound(personId, cardClass, sequenceNumber);

        RequireExpectedVersion(expectedVersion, current.Version);

        if (current.Status != CardIssueStatus.Active)
        {
            throw CardIssueError.InvalidTransition("deactivate", current.Status);
        }

        var now = UtcInstant.Now();
        var updated = current with { Status = CardIssueStatus.Deactivated, DeactivatedAt = now, Version = current.Version + 1, UpdatedAt = now };

        await AuditAsync(tenantId, actor, "card_issue.deactivate", personId, cardClass, sequenceNumber, "card_deactivated", correlationId, causationId: null,
            detail: new Dictionary<string, (object?, string)> { ["sequence_number"] = (sequenceNumber, "internal") },
            cancellationToken);

        return repository.Replace(current, updated);
    }

    private static void RequireExpectedVersion(int? expectedVersion, int actualVersion)
    {
        if (expectedVersion is not int expected)
        {
            throw CardIssueError.PreconditionRequired();
        }

        if (expected != actualVersion)
        {
            throw CardIssueError.ConcurrencyConflict(expected, actualVersion);
        }
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, string cardClass, int sequenceNumber, string reasonCode,
        string correlationId, string? causationId, IReadOnlyDictionary<string, (object? Value, string Classification)> detail,
        CancellationToken cancellationToken)
    {
        var auditEvent = CardIssueAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("card_issue", $"{personId}:{cardClass}:{sequenceNumber}"),
            AuditOutcome.Allowed, reasonCode, correlationId, causationId, detail);

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
            throw CardIssueError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw CardIssueError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as a
        // success, so the caller's subsequent commit makes this event's claim true.
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw CardIssueError.TenantContextMissing();

    private static string? ValidateRequiredField(string? value, string field, List<Api.ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new Api.ValidationErrorItem(field, "required", $"{field} is required"));
            return null;
        }

        return value.Trim();
    }

    private static void ThrowIfInvalid(List<Api.ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw CardIssueError.Validation(errors);
        }
    }
}

/// <summary>Request to issue a new physical card (BIO-CRD-005-AC05's provenance inputs). Lives here, not in Api/, since it is this service's own application-layer input shape - Api/CardIssueDtos.cs maps the wire request onto it.</summary>
public sealed record IssueCardRequest
{
    public required string TemplateRef { get; init; }
    public string? PhotoRef { get; init; }
    public string? LogoRef { get; init; }
    public string? SignatureRef { get; init; }
    public required string PrinterRef { get; init; }
}
