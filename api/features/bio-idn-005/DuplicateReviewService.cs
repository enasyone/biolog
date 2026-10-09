using Biolog.Features.DuplicateReview.Api;
using Biolog.Features.DuplicateReview.Audit;
using Biolog.Features.DuplicateReview.Domain;
using Biolog.Features.DuplicateReview.Identifiers;
using Biolog.Features.DuplicateReview.Tenancy;

namespace Biolog.Features.DuplicateReview;

/// <summary>
/// Application service for BIO-IDN-005 ("Implement duplicate review foundation"). Implements
/// ADR-0003 Decision 7's audit-atomicity pattern exactly as established across every sibling
/// story in this epic: every mutating method holds an in-process, per-pair lock
/// (<see cref="DuplicateCandidateLockRegistry"/>) across its read-validate-audit-commit
/// sequence, audits BEFORE committing, resolves an ambiguous audit-write throw via
/// <see cref="IDuplicateCandidateAuditSink.ExistsAsync"/> rather than assuming failure, and
/// passes <see cref="CancellationToken.None"/> to the repository commit once the audit write is
/// confirmed durable.
///
/// Registered Scoped, like every sibling identity-wave service, for the identical reason: this
/// service reads a per-request <see cref="ICurrentTenantProvider"/>, so it cannot be Singleton -
/// the lock table lives in its own Singleton class, <see cref="DuplicateCandidateLockRegistry"/>.
///
/// <b>AC01 - candidate duplicates arise from identifiers/contact data and later biometric
/// signals.</b> <see cref="ScanForPersonAsync"/> compares the subject against every OTHER person
/// in the resolved tenant on three signals available today: an exact, case-insensitive match of
/// (first name, last name, date of birth) via <see cref="IPersonAttributeGateway"/>
/// (<see cref="DuplicateSignalSource.NameAndDateOfBirth"/>), a shared ACTIVE institutional
/// identifier value via <see cref="IIdentifierAttributeGateway"/>
/// (<see cref="DuplicateSignalSource.InstitutionalIdentifierValue"/>), and a shared normalized
/// email or phone value on file via <see cref="IPersonContactPointRepository"/>
/// (<see cref="DuplicateSignalSource.ContactData"/> - see
/// <b>BIO-IDN-005-QA-01</b> below for why this is a genuinely real contact-data signal, not a
/// relabeling of the name/date-of-birth one).
/// <see cref="DuplicateSignalSource.Biometric"/> is reserved for a future story in this same
/// "duplicate-review" conflict group (BIO-BIO-001/002) - no code path in this service produces
/// it, but <see cref="RaiseOrAugmentAsync"/>'s signal-accumulation design does not need to
/// change to accept it later (see that enum's header).
///
/// <b>BIO-IDN-005-QA-01 (Seyi round 1, High).</b> The round 1 submission treated
/// <see cref="DuplicateSignalSource.NameAndDateOfBirth"/> as this story's "contact data" signal
/// entirely, because PERSON (BIO-IDN-001) has no email/phone/address field at all. The review
/// correctly rejected this - a name and date of birth are identity/demographic attributes, not
/// contact information, and a lower-level implementation note cannot redefine an approved
/// acceptance criterion. Since BIO-IDN-001 is already ACCEPTED and merged (reopening it is a
/// cross-story governance question this story does not need to raise), the fix instead gives
/// this story its OWN genuinely real, tenant-scoped contact-data source -
/// <see cref="Domain.PersonContactPoint"/>, set via <see cref="PersonContactPointService"/> -
/// mirroring the exact precedent BIO-IDN-003 already set for "institutional identifier" (a
/// sibling aggregate linked by <c>PersonId</c>, never a field bolted onto PERSON itself).
/// <see cref="ScanForPersonAsync"/> now reads it directly (no gateway/adapter seam needed - this
/// aggregate is owned by THIS project, not a sibling story's store) and raises
/// <see cref="DuplicateSignalSource.ContactData"/> when the subject and another person's
/// contact point share a non-blank, normalized email or phone value.
///
/// <b>AC02 - review decisions are human-audited.</b> Both the scan (raising/augmenting a
/// candidate) and <see cref="DecideAsync"/> require an asserted <see cref="Audit.AuditActor"/> -
/// the staff member who triggered the scan, and the reviewer who recorded the decision - and
/// both write an append-only <see cref="DuplicateCandidateAuditEvent"/> before committing.
///
/// <b>AC03 - merge is never automatic.</b> Nothing in this class, this project, or any endpoint
/// it maps calls into BIO-IDN-001's <c>PersonService</c> to mutate, archive or delete a PERSON
/// record - this project has no <c>ProjectReference</c> to BIO-IDN-001's project at all (see
/// <see cref="IPersonAttributeGateway"/>'s header), so it structurally cannot.
/// <see cref="DecideAsync"/> only ever records the human's decision on THIS feature's own
/// <see cref="DuplicateCandidate"/> record - see docs/features/bio-idn-005/README.md "AC03 - no
/// merge exists in this codebase".
///
/// <b>AC04 - cross-tenant duplicate search is prohibited by default.</b> Every candidate signal
/// this service reads comes through <see cref="IPersonAttributeGateway"/>/
/// <see cref="IIdentifierAttributeGateway"/>, both of which wrap an already tenant-scoped
/// sibling service (BIO-IDN-001's/BIO-IDN-003's own <c>ICurrentTenantProvider</c>-scoped stores)
/// - so a person that exists only in a different tenant is never returned as a comparison
/// candidate, and <see cref="IPersonAttributeGateway.GetAsync"/> resolves a subject id that
/// belongs to a different tenant as not-found (ADR-0001 §1.5, no enumeration oracle), exactly
/// mirroring BIO-IDN-002/003/004's own existence-gateway behaviour. This service accepts no
/// parameter, header or flag that would widen a scan beyond the resolved tenant - "by default"
/// leaves room for a FUTURE, separately-authorised admin capability; this story does not build
/// one.
///
/// <b>Never re-detects a decided pair.</b> Once a candidate has any decision
/// (<see cref="DuplicateReviewStatus.ConfirmedDuplicate"/> or
/// <see cref="DuplicateReviewStatus.ConfirmedDistinct"/>), a later scan that would otherwise
/// re-raise or augment it leaves the record untouched instead (see
/// <see cref="RaiseOrAugmentAsync"/>) - a human already reviewed this pair, and a routine
/// rescan must never silently reopen or relitigate that call. A reviewer can still change a
/// decision by calling <see cref="DecideAsync"/> again directly (fully audited either way) -
/// this restriction is only against an automated scan overriding a human's own choice.
///
/// <b>Audit trail privacy.</b> This feature's own audit <c>Detail</c> never carries a compared
/// PII value (a name or date of birth) - only person ids and the WIRE LABEL of which signal
/// matched (see <see cref="DuplicateCandidateAuditEventBuilder"/>'s header, which registers no
/// classification above <c>internal</c> for exactly this reason).
/// </summary>
internal sealed class DuplicateReviewService(
    ICurrentTenantProvider tenantProvider,
    IDuplicateCandidateRepository repository,
    IDuplicateCandidateAuditSink auditSink,
    DuplicateCandidateLockRegistry lockRegistry,
    IPersonAttributeGateway personGateway,
    IIdentifierAttributeGateway identifierGateway,
    IPersonContactPointRepository contactPointRepository)
{
    public async Task<IReadOnlyList<DuplicateCandidate>> ScanForPersonAsync(
        string personId, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!CanonicalId.IsPersonId(personId))
        {
            throw DuplicateCandidateError.Validation([new ValidationErrorItem("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier")]);
        }

        var subject = await personGateway.GetAsync(personId, cancellationToken) ?? throw DuplicateCandidateError.PersonNotFound(personId);
        var subjectIdentifiers = await identifierGateway.ListActiveValuesAsync(personId, cancellationToken);
        var subjectContact = await contactPointRepository.GetAsync(tenantId, personId, cancellationToken);
        var others = await personGateway.ListOthersAsync(personId, cancellationToken);

        var raised = new List<DuplicateCandidate>();
        foreach (var other in others)
        {
            var signals = new HashSet<DuplicateSignalSource>();

            if (IsSameNameAndDateOfBirth(subject, other))
            {
                signals.Add(DuplicateSignalSource.NameAndDateOfBirth);
            }

            var otherIdentifiers = await identifierGateway.ListActiveValuesAsync(other.PersonId, cancellationToken);
            if (SharesAnyIdentifierValue(subjectIdentifiers, otherIdentifiers))
            {
                signals.Add(DuplicateSignalSource.InstitutionalIdentifierValue);
            }

            var otherContact = await contactPointRepository.GetAsync(tenantId, other.PersonId, cancellationToken);
            if (SharesContactData(subjectContact, otherContact))
            {
                signals.Add(DuplicateSignalSource.ContactData);
            }

            if (signals.Count == 0)
            {
                continue;
            }

            var candidate = await RaiseOrAugmentAsync(tenantId, personId, other.PersonId, signals, actor, correlationId, cancellationToken);
            if (candidate is not null)
            {
                raised.Add(candidate);
            }
        }

        return raised;
    }

    /// <summary>AC02's review queue: every candidate involving this person, in either pair position.</summary>
    public Task<IReadOnlyList<DuplicateCandidate>> ListForPersonAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.ListForPersonAsync(tenantId, personId, cancellationToken);
    }

    /// <summary>AC02/AC03: records a human review decision. Never creates, deletes or touches a PERSON record - see class header.</summary>
    public async Task<DuplicateCandidate> DecideAsync(
        string personIdA, string personIdB, DecideDuplicateCandidateRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var errors = new List<ValidationErrorItem>();
        if (!CanonicalId.IsPersonId(personIdA) || !CanonicalId.IsPersonId(personIdB))
        {
            errors.Add(new ValidationErrorItem("person_id", "invalid_format", "both person ids must be well-formed canonical per_ identifiers"));
        }

        if (!DuplicateReviewStatusExtensions.TryParse(request.Decision, out var decision)
            || (decision != DuplicateReviewStatus.ConfirmedDuplicate && decision != DuplicateReviewStatus.ConfirmedDistinct))
        {
            errors.Add(new ValidationErrorItem("decision", "invalid_format", "decision must be CONFIRMED_DUPLICATE or CONFIRMED_DISTINCT"));
        }

        var reasonCode = ValidateReasonCode(request.ReasonCode, errors);
        var notes = ValidateNotes(request.Notes, errors);

        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
            expectedVersion = default;
        }

        ThrowIfInvalid(errors);

        var (personIdLow, personIdHigh) = DuplicateCandidate.OrderPair(personIdA, personIdB);

        using var _ = await lockRegistry.AcquireAsync(tenantId, personIdLow, personIdHigh, cancellationToken);
        var current = await repository.GetAsync(tenantId, personIdLow, personIdHigh, cancellationToken)
            ?? throw DuplicateCandidateError.NotFound(tenantId, personIdLow, personIdHigh);

        if (expectedVersion != current.Version)
        {
            throw DuplicateCandidateError.ConcurrencyConflict(personIdLow, personIdHigh, expectedVersion, current.Version);
        }

        var now = UtcInstant.Now();
        var updated = current with
        {
            Status = decision,
            DecidedAt = now,
            DecidedByActorId = actor.Id,
            DecisionReasonCode = reasonCode,
            DecisionNotes = notes,
            Version = current.Version + 1,
            UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "duplicate_candidate.decide", personIdLow, personIdHigh, reasonCode!, correlationId, cancellationToken);

        // Guaranteed to succeed: the per-pair lock held since entry means no other writer in
        // this process could have changed `current` since it was read. CancellationToken is
        // None: the audit event above is already confirmed durable (see class header).
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    /// <summary>
    /// BIO-IDN-003-QA-01's lesson applied from the start: the lock scope here is the PAIR
    /// itself, not some broader population - see <see cref="DuplicateCandidateLockRegistry"/>'s
    /// header for why that is the correct scope for this story's own invariants.
    ///
    /// Returns <c>null</c> (nothing raised or changed) when the pair already has a human
    /// decision, or when the newly-observed signals add nothing this candidate does not
    /// already record - see class header "Never re-detects a decided pair".
    /// </summary>
    private async Task<DuplicateCandidate?> RaiseOrAugmentAsync(
        string tenantId, string personIdA, string personIdB, IReadOnlySet<DuplicateSignalSource> signals, AuditActor actor, string correlationId,
        CancellationToken cancellationToken)
    {
        var (personIdLow, personIdHigh) = DuplicateCandidate.OrderPair(personIdA, personIdB);

        using var _ = await lockRegistry.AcquireAsync(tenantId, personIdLow, personIdHigh, cancellationToken);
        var existing = await repository.GetAsync(tenantId, personIdLow, personIdHigh, cancellationToken);

        if (existing is null)
        {
            var now = UtcInstant.Now();
            var candidate = new DuplicateCandidate(tenantId, personIdLow, personIdHigh)
            {
                SignalSources = signals,
                Status = DuplicateReviewStatus.PendingReview,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };

            await AuditAsync(tenantId, actor, "duplicate_candidate.raise", personIdLow, personIdHigh, "duplicate_candidate_raised", correlationId, cancellationToken, signals);
            return await repository.CreateAsync(candidate, CancellationToken.None);
        }

        if (existing.Status != DuplicateReviewStatus.PendingReview)
        {
            // A human already reviewed this pair - a routine rescan must never silently
            // reopen or relitigate that call (see class header).
            return null;
        }

        if (signals.IsSubsetOf(existing.SignalSources))
        {
            // Nothing new to record - avoid audit noise on every rescan re-detecting the
            // same, already-known signal.
            return existing;
        }

        var augmentedSignals = new HashSet<DuplicateSignalSource>(existing.SignalSources);
        augmentedSignals.UnionWith(signals);
        var updated = existing with { SignalSources = augmentedSignals, Version = existing.Version + 1, UpdatedAt = UtcInstant.Now() };

        await AuditAsync(tenantId, actor, "duplicate_candidate.augment", personIdLow, personIdHigh, "duplicate_candidate_augmented", correlationId, cancellationToken, signals);
        return await repository.ReplaceAsync(existing, updated, CancellationToken.None);
    }

    private static bool IsSameNameAndDateOfBirth(PersonAttributes subject, PersonAttributes other)
    {
        if (subject.BornOn is null || other.BornOn is null || subject.BornOn != other.BornOn)
        {
            return false;
        }

        return string.Equals(subject.FirstName.Trim(), other.FirstName.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(subject.LastName.Trim(), other.LastName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SharesAnyIdentifierValue(IReadOnlyList<IdentifierAttribute> subject, IReadOnlyList<IdentifierAttribute> other) =>
        subject.Any(s => other.Any(o => o.Type == s.Type && o.CustomLabel == s.CustomLabel && string.Equals(o.Value, s.Value, StringComparison.Ordinal)));

    /// <summary>
    /// BIO-IDN-005-QA-01: a genuine contact-data signal. Email is already stored normalized
    /// (trimmed, lower-cased - see <see cref="PersonContactPoint"/>'s header), compared exactly.
    /// Phone is normalized HERE, at comparison time, by stripping every non-digit character - a
    /// deliberate simplification that does not attempt international dialing-code/country-code
    /// reconciliation (documented on <see cref="PersonContactPoint.Phone"/>). A blank value on
    /// either side never counts as a match (two people with no phone on file are not thereby a
    /// "match").
    /// </summary>
    private static bool SharesContactData(PersonContactPoint? subject, PersonContactPoint? other)
    {
        if (subject is null || other is null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(subject.Email) && string.Equals(subject.Email, other.Email, StringComparison.Ordinal))
        {
            return true;
        }

        var subjectDigits = NormalizePhone(subject.Phone);
        var otherDigits = NormalizePhone(other.Phone);
        return subjectDigits.Length > 0 && string.Equals(subjectDigits, otherDigits, StringComparison.Ordinal);
    }

    private static string NormalizePhone(string? phone) => phone is null ? string.Empty : new string([.. phone.Where(char.IsDigit)]);

    /// <summary>
    /// Durably records this action as a <see cref="DuplicateCandidateAuditEvent"/>, resolving an
    /// ambiguous <see cref="IDuplicateCandidateAuditSink.WriteAsync"/> throw via
    /// <see cref="IDuplicateCandidateAuditSink.ExistsAsync"/> before treating it as a real
    /// failure (ADR-0003 Decision 7, element 4). <paramref name="signals"/>, when supplied, is
    /// recorded only as its WIRE LABELS - never a compared name or date of birth value (see
    /// class header "Audit trail privacy").
    /// </summary>
    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personIdLow, string personIdHigh, string reasonCode, string correlationId,
        CancellationToken cancellationToken, IReadOnlySet<DuplicateSignalSource>? signals = null)
    {
        var target = new AuditTarget("duplicate_candidate", $"{personIdLow}/{personIdHigh}");
        var detail = new Dictionary<string, (object?, string)>
        {
            ["person_id_low"] = (personIdLow, "internal"),
            ["person_id_high"] = (personIdHigh, "internal"),
        };

        if (signals is not null)
        {
            detail["signal_sources"] = (signals.Select(s => s.ToWireString()).OrderBy(s => s, StringComparer.Ordinal).ToArray(), "internal");
        }

        var auditEvent = DuplicateCandidateAuditEventBuilder.Build(
            tenantId, actor, action, target, AuditOutcome.Allowed, reasonCode, correlationId, causationId: null, detail, withheld: []);

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

    private static string? ValidateReasonCode(string? value, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem("reason_code", "required", "reason_code is required to record a review decision"));
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 100)
        {
            errors.Add(new ValidationErrorItem("reason_code", "out_of_range", "reason_code must be at most 100 characters"));
            return null;
        }

        return trimmed;
    }

    private static string? ValidateNotes(string? value, List<ValidationErrorItem> errors)
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

        if (trimmed.Length > 1000)
        {
            errors.Add(new ValidationErrorItem("notes", "out_of_range", "notes must be at most 1000 characters"));
            return null;
        }

        return trimmed;
    }
}
