using Biolog.Features.CardProduction.Audit;
using Biolog.Features.CardProduction.Domain;
using Biolog.Features.CardProduction.Identifiers;
using Biolog.Features.CardProduction.Tenancy;

namespace Biolog.Features.CardProduction;

/// <summary>
/// Application service for BIO-CRD-007. Applies the same ADR-0003 Decision 7 audit-atomicity
/// pattern every sibling story in this epic uses: mutations hold a per-(tenant, person, card
/// class, sequence number) lock across their own read-validate-audit-commit sequence, audit
/// BEFORE committing, and resolve an ambiguous audit-write throw via
/// <see cref="IProductionAuditSink.ExistsAsync"/> rather than assuming failure.
///
/// <b>BIO-CRD-007-AC02/AC05: activation is gated, never re-implemented.</b>
/// <see cref="ActivateAfterProductionAsync"/> refuses to even attempt activation unless the
/// most recent production job for this card has resolved to
/// <see cref="ProductionJobStatus.Passed"/>, then delegates to BIO-CRD-005's own real,
/// authoritative <see cref="ICardIssueGateway.ActivateAsync"/> - which already enforces "at
/// most one Active card" (AC05). Unlike BIO-CRD-006's own activate-replacement, this method has
/// NO local, irreversible side effect of its own before that delegate call (it only reads the
/// latest job's status), so there is nothing here for BIO-CRD-006-QA-01/02/03's own
/// audit-before-commit / precondition-before-side-effect / compensate-on-race lessons to apply
/// to - a bad or stale request is simply refused, and a raced concurrent gateway change is
/// caught by the gateway's own compare-and-set, exactly as it already is for every other caller
/// of that same real endpoint.
///
/// <b>BIO-CRD-007-AC03: the offline-complete race.</b> <see cref="CompleteOfflineJobAsync"/>
/// verifies a package's signature and parses its payload (a pure computation, no lock needed),
/// but only checks the package's own <see cref="ProductionPackage.Consumed"/> flag and creates
/// the resulting job AFTER acquiring the SAME per-card lock every other mutation on this card
/// already serialises against - a signed package always names exactly one card, so that lock is
/// what prevents two concurrent submissions of the identical token from both observing
/// "not yet consumed" and both creating a job.
///
/// <b>BIO-OPS-003-AC04 round 2 (SY-OPS003-02, Kemi's ruling).</b> This story's own
/// <see cref="ProductionJobLockRegistry"/> never excluded BIO-CRD-006's own
/// <c>ActivateReplacementAsync</c> from concurrently reading this story's latest job status and
/// later activating against it - a stale-snapshot race, since the two features' locks are fully
/// independent. EVERY method below that acquires <see cref="ProductionJobLockRegistry"/> for a
/// key now ALSO acquires <see cref="Domain.ICardActivationBarrier"/> for that same key,
/// immediately after, nested the same way - see that interface's own header for the shared,
/// host-layer lock it reaches and why this is stated as an invariant over every acquisition
/// site, not a fixed list.
/// </summary>
internal sealed class ProductionService(
    ICurrentTenantProvider tenantProvider,
    IProductionJobRepository repository,
    IProductionPackageStore packageStore,
    IProductionSigningKeyStore signingKeyStore,
    ICardIssueGateway cardIssueGateway,
    IProductionAuditSink auditSink,
    ProductionJobLockRegistry lockRegistry,
    IIdempotencyStore idempotencyStore,
    ICardActivationBarrier activationBarrier,
    TimeSpan? idempotencyWaitTimeout = null,
    TimeSpan? idempotencyWaitPollInterval = null)
{
    private const string CompleteOfflineJobOperation = "complete_offline_job";
    private static readonly TimeSpan DefaultPackageTtl = TimeSpan.FromHours(4);
    private static readonly TimeSpan MaxPackageTtl = TimeSpan.FromHours(24);

    // TD-CRD-007-IDEMPOTENCY-RACE's own remediation: how long a claim-loser (a concurrent caller
    // whose Idempotency-Key is already owned by someone else's in-flight attempt) waits for that
    // owner's result before giving up with a 503 (ADR-0004/BIO-ARC-004's documented "claim-loser
    // waits bounded then idempotency_wait_timeout" contract). Both are injectable (constructor
    // parameters, defaulting to production-sized values) so a test can prove the timeout path
    // deterministically and fast, without actually waiting multiple real seconds.
    private readonly TimeSpan _idempotencyWaitTimeout = idempotencyWaitTimeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _idempotencyWaitPollInterval = idempotencyWaitPollInterval ?? TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// BIO-CRD-007-AC01/AC04: starts a new, connected production job. A reprint (job sequence
    /// &gt; 1) must record why; a first attempt must not.
    ///
    /// <paramref name="templateId"/>/<paramref name="templateVersion"/> are additive and optional
    /// (BIO-CRD-008-AC04) - a plain pass-through, not validated here. The caller is expected to
    /// have obtained this exact pair from BIO-CRD-008's own <c>CardTemplateService.GetPublished</c>
    /// (the one sanctioned source of "the exact published version"); this method does not gateway
    /// into that already-accepted story's own DI graph to re-check it.
    /// </summary>
    public async Task<ProductionJob> CreateJobAsync(
        string personId, string cardClass, int sequenceNumber, string printerRef, string workstationRef, string? reprintReason,
        AuditActor actor, string correlationId, CancellationToken cancellationToken = default,
        string? templateId = null, int? templateVersion = null)
    {
        var tenantId = RequireTenant();

        var errors = new List<Api.ValidationErrorItem>();
        var normalisedPrinter = ValidateRequiredField(printerRef, "printer_ref", errors);
        var normalisedWorkstation = ValidateRequiredField(workstationRef, "workstation_ref", errors);
        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);
        using var __ = await activationBarrier.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);

        if (await cardIssueGateway.GetAsync(personId, cardClass, sequenceNumber, cancellationToken) is null)
        {
            throw ProductionError.CardIssueNotFound(personId, cardClass, sequenceNumber);
        }

        EnsureNoOpenJob(tenantId, personId, cardClass, sequenceNumber);

        var jobSequenceNumber = repository.NextJobSequenceNumber(tenantId, personId, cardClass, sequenceNumber);
        var normalisedReprintReason = ValidateReprintReason(jobSequenceNumber, reprintReason);

        var now = UtcInstant.Now();
        var job = new ProductionJob(tenantId, personId, cardClass, sequenceNumber, jobSequenceNumber)
        {
            Status = ProductionJobStatus.InProgress,
            Version = 1,
            PrinterRef = normalisedPrinter!,
            Operator = actor,
            WorkstationRef = normalisedWorkstation!,
            ReprintReason = normalisedReprintReason,
            Source = "connected",
            TemplateId = templateId,
            TemplateVersion = templateVersion,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "production.create_job", personId, cardClass, sequenceNumber, jobSequenceNumber, "production_job_created", correlationId,
            new Dictionary<string, (object?, string)> { ["job_sequence_number"] = (jobSequenceNumber, "internal"), ["printer_ref"] = (normalisedPrinter, "internal") },
            cancellationToken);

        return repository.Create(job);
    }

    public Task<ProductionJob> RecordPrintAsync(string personId, string cardClass, int sequenceNumber, int jobSequenceNumber, bool passed, string? detail, int? expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        RecordStepAsync(personId, cardClass, sequenceNumber, jobSequenceNumber, expectedVersion, actor, correlationId,
            step: "print", apply: (job, result) => job with { Print = result }, getResult: job => job.Print, cancellationToken, passed, detail);

    public Task<ProductionJob> RecordEncodeAsync(string personId, string cardClass, int sequenceNumber, int jobSequenceNumber, bool passed, string? detail, int? expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        RecordStepAsync(personId, cardClass, sequenceNumber, jobSequenceNumber, expectedVersion, actor, correlationId,
            step: "encode", apply: (job, result) => job with { Encode = result }, getResult: job => job.Encode, cancellationToken, passed, detail);

    public Task<ProductionJob> RecordReadBackQcAsync(string personId, string cardClass, int sequenceNumber, int jobSequenceNumber, bool passed, string? detail, int? expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        RecordStepAsync(personId, cardClass, sequenceNumber, jobSequenceNumber, expectedVersion, actor, correlationId,
            step: "readback_qc", apply: (job, result) => job with { ReadBackQc = result }, getResult: job => job.ReadBackQc, cancellationToken, passed, detail);

    private async Task<ProductionJob> RecordStepAsync(
        string personId, string cardClass, int sequenceNumber, int jobSequenceNumber, int? expectedVersion, AuditActor actor, string correlationId,
        string step, Func<ProductionJob, ProductionStepResult, ProductionJob> apply, Func<ProductionJob, ProductionStepResult?> getResult,
        CancellationToken cancellationToken, bool passed, string? detail)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);
        using var __ = await activationBarrier.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);

        var current = repository.Get(tenantId, personId, cardClass, sequenceNumber, jobSequenceNumber)
            ?? throw ProductionError.JobNotFound(personId, cardClass, sequenceNumber, jobSequenceNumber);

        RequireExpectedVersion(expectedVersion, current.Version);

        if (!current.Status.IsOpen())
        {
            throw ProductionError.JobNotOpen(step, current.Status);
        }

        if (getResult(current) is not null)
        {
            throw ProductionError.StepAlreadyRecorded(step);
        }

        var now = UtcInstant.Now();
        var result = new ProductionStepResult { Passed = passed, Detail = detail, RecordedAt = now };
        var updated = apply(current, result) with { Version = current.Version + 1, UpdatedAt = now };
        updated = updated with { Status = ResolveStatus(updated) };

        await AuditAsync(tenantId, actor, $"production.{step}", personId, cardClass, sequenceNumber, jobSequenceNumber, passed ? $"{step}_passed" : $"{step}_failed", correlationId,
            new Dictionary<string, (object?, string)> { ["job_sequence_number"] = (jobSequenceNumber, "internal"), ["passed"] = (passed, "internal") },
            cancellationToken);

        return repository.Replace(current, updated);
    }

    /// <summary>BIO-CRD-007-AC02: derives the job's own overall outcome from its three recorded steps - Passed only if every one of them passed, Failed if any one of them did not. Stays InProgress until all three have been recorded.</summary>
    private static ProductionJobStatus ResolveStatus(ProductionJob job)
    {
        if (job.Print is null || job.Encode is null || job.ReadBackQc is null)
        {
            return ProductionJobStatus.InProgress;
        }

        return job.Print.Passed && job.Encode.Passed && job.ReadBackQc.Passed
            ? ProductionJobStatus.Passed
            : ProductionJobStatus.Failed;
    }

    /// <summary>AC04: an explicit rejection - a card that mechanically passed every step but is visually/physically flawed.</summary>
    public async Task<ProductionJob> RejectJobAsync(string personId, string cardClass, int sequenceNumber, int jobSequenceNumber, string reason, int? expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var errors = new List<Api.ValidationErrorItem>();
        var normalisedReason = ValidateRequiredField(reason, "reason", errors);
        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);
        using var __ = await activationBarrier.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);

        var current = repository.Get(tenantId, personId, cardClass, sequenceNumber, jobSequenceNumber)
            ?? throw ProductionError.JobNotFound(personId, cardClass, sequenceNumber, jobSequenceNumber);

        RequireExpectedVersion(expectedVersion, current.Version);

        if (current.Status != ProductionJobStatus.InProgress && current.Status != ProductionJobStatus.Passed)
        {
            throw ProductionError.JobNotOpen("reject", current.Status);
        }

        var now = UtcInstant.Now();
        var updated = current with { Status = ProductionJobStatus.Rejected, RejectionReason = normalisedReason, Version = current.Version + 1, UpdatedAt = now };

        await AuditAsync(tenantId, actor, "production.reject_job", personId, cardClass, sequenceNumber, jobSequenceNumber, "production_job_rejected", correlationId,
            new Dictionary<string, (object?, string)> { ["job_sequence_number"] = (jobSequenceNumber, "internal"), ["reason"] = (normalisedReason, "internal") },
            cancellationToken);

        return repository.Replace(current, updated);
    }

    /// <summary>Necessary, disclosed addition (mirrors BIO-CRD-005's own precedent) - a way to close out a job queued in error, e.g. the wrong card was queued.</summary>
    public async Task<ProductionJob> CancelJobAsync(string personId, string cardClass, int sequenceNumber, int jobSequenceNumber, int? expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);
        using var __ = await activationBarrier.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);

        var current = repository.Get(tenantId, personId, cardClass, sequenceNumber, jobSequenceNumber)
            ?? throw ProductionError.JobNotFound(personId, cardClass, sequenceNumber, jobSequenceNumber);

        RequireExpectedVersion(expectedVersion, current.Version);

        if (current.Status != ProductionJobStatus.InProgress)
        {
            throw ProductionError.JobNotOpen("cancel", current.Status);
        }

        var now = UtcInstant.Now();
        var updated = current with { Status = ProductionJobStatus.Cancelled, Version = current.Version + 1, UpdatedAt = now };

        await AuditAsync(tenantId, actor, "production.cancel_job", personId, cardClass, sequenceNumber, jobSequenceNumber, "production_job_cancelled", correlationId,
            new Dictionary<string, (object?, string)> { ["job_sequence_number"] = (jobSequenceNumber, "internal") }, cancellationToken);

        return repository.Replace(current, updated);
    }

    public Task<ProductionJob?> GetJobAsync(string personId, string cardClass, int sequenceNumber, int jobSequenceNumber, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return Task.FromResult(repository.Get(tenantId, personId, cardClass, sequenceNumber, jobSequenceNumber));
    }

    /// <summary>AC04: the full production report for this card - every attempt, in order.</summary>
    public Task<IReadOnlyList<ProductionJob>> ListJobsAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return Task.FromResult(repository.List(tenantId, personId, cardClass, sequenceNumber));
    }

    /// <summary>BIO-CRD-007-AC03: issues a signed, time-limited authorisation an offline production station can act on without live connectivity.</summary>
    public async Task<(ProductionPackage Package, string Token)> IssuePackageAsync(
        string personId, string cardClass, int sequenceNumber, string printerRef, string workstationRef, string? reprintReason, TimeSpan? ttl,
        AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var errors = new List<Api.ValidationErrorItem>();
        var normalisedPrinter = ValidateRequiredField(printerRef, "printer_ref", errors);
        var normalisedWorkstation = ValidateRequiredField(workstationRef, "workstation_ref", errors);
        ThrowIfInvalid(errors);

        var effectiveTtl = ttl ?? DefaultPackageTtl;
        if (effectiveTtl <= TimeSpan.Zero || effectiveTtl > MaxPackageTtl)
        {
            throw ProductionError.Validation([new Api.ValidationErrorItem("ttl_minutes", "out_of_range", $"ttl_minutes must be between 1 and {(int)MaxPackageTtl.TotalMinutes}")]);
        }

        if (await cardIssueGateway.GetAsync(personId, cardClass, sequenceNumber, cancellationToken) is null)
        {
            throw ProductionError.CardIssueNotFound(personId, cardClass, sequenceNumber);
        }

        var now = DateTime.UtcNow;
        var package = new ProductionPackage
        {
            PackageId = Guid.NewGuid().ToString("D"),
            TenantId = tenantId,
            PersonId = personId,
            CardClass = cardClass,
            SequenceNumber = sequenceNumber,
            PrinterRef = normalisedPrinter!,
            Operator = actor,
            WorkstationRef = normalisedWorkstation!,
            ReprintReason = string.IsNullOrWhiteSpace(reprintReason) ? null : reprintReason.Trim(),
            IssuedAt = UtcInstant.Render(now),
            ExpiresAt = UtcInstant.Render(now + effectiveTtl),
            Version = 1,
            Consumed = false,
        };

        await AuditAsync(tenantId, actor, "production.issue_package", personId, cardClass, sequenceNumber, jobSequenceNumber: null, "production_package_issued", correlationId,
            new Dictionary<string, (object?, string)> { ["package_id"] = (package.PackageId, "internal"), ["expires_at"] = (package.ExpiresAt, "internal") },
            cancellationToken);

        packageStore.Create(package);
        var token = ProductionPackageSigner.BuildToken(package, signingKeyStore.GetOrCreateKey(tenantId));
        return (package, token);
    }

    /// <summary>BIO-CRD-007-AC03: an offline station's own full report, submitted once connectivity resumes. ADR-0004 §4.3's own named "offline sync intake" - Idempotency-Key required.</summary>
    public async Task<ProductionJob> CompleteOfflineJobAsync(
        string token, bool printPassed, string? printDetail, bool encodePassed, string? encodeDetail, bool readBackQcPassed, string? readBackQcDetail,
        string idempotencyKey, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw ProductionError.IdempotencyKeyRequired();
        }

        // TD-CRD-007-IDEMPOTENCY-RACE: the original code checked only `claimed.State ==
        // Completed` - a shared InFlight record from a CONCURRENT caller looked identical to a
        // claim this caller itself just won, so a second caller fell through and executed a
        // second time regardless. `IdempotencyClaim.IsNewClaim` is the missing ownership signal:
        // only the true owner ever breaks out of this loop to proceed below. Every other caller
        // (someone else's claim is in flight) waits, bounded by `_idempotencyWaitTimeout`,
        // re-attempting its own `Claim` each iteration.
        var fingerprint = $"{token}\u0000{printPassed}\u0000{encodePassed}\u0000{readBackQcPassed}";
        var deadline = DateTime.UtcNow + _idempotencyWaitTimeout;
        while (true)
        {
            var claim = idempotencyStore.Claim(tenantId, CompleteOfflineJobOperation, idempotencyKey, fingerprint);
            if (claim.Record.RequestFingerprint != fingerprint)
            {
                throw ProductionError.IdempotencyKeyReuse();
            }

            if (claim.Record.State == IdempotencyRecordState.Completed)
            {
                return (ProductionJob)claim.Record.Result!;
            }

            if (claim.IsNewClaim)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw ProductionError.IdempotencyWaitTimeout(idempotencyKey);
            }

            await Task.Delay(_idempotencyWaitPollInterval, cancellationToken);
        }

        try
        {
            // Verifying the signature and parsing the payload is a pure computation - no lock
            // needed yet, and it is what tells us WHICH card's lock to acquire next.
            if (!ProductionPackageSigner.TryVerifyAndParse(token, signingKeyStore.GetOrCreateKey(tenantId), out var payload))
            {
                throw ProductionError.PackageSignatureInvalid();
            }

            if (payload!.TenantId != tenantId)
            {
                // Defence in depth only - unreachable in practice, since verification above
                // already used THIS tenant's own random key, and no other tenant's package
                // could ever produce a matching signature under it.
                throw ProductionError.PackageTenantMismatch();
            }

            // Every check and mutation from here on happens under the SAME per-card lock every
            // other mutation on this card already serialises against - this is what prevents
            // two concurrent submissions of the identical token from both observing
            // "not yet consumed" and both creating a job (see class header).
            using var _ = await lockRegistry.AcquireAsync(tenantId, payload.PersonId, payload.CardClass, payload.SequenceNumber, cancellationToken);
            using var __ = await activationBarrier.AcquireAsync(tenantId, payload.PersonId, payload.CardClass, payload.SequenceNumber, cancellationToken);

            if (string.CompareOrdinal(UtcInstant.Now(), payload.ExpiresAt) > 0)
            {
                throw ProductionError.PackageExpired(payload.ExpiresAt);
            }

            var storedPackage = packageStore.Get(tenantId, payload.PackageId) ?? throw ProductionError.PackageNotFound(payload.PackageId);
            if (storedPackage.Consumed)
            {
                throw ProductionError.PackageAlreadyConsumed(payload.PackageId);
            }

            if (await cardIssueGateway.GetAsync(payload.PersonId, payload.CardClass, payload.SequenceNumber, cancellationToken) is null)
            {
                throw ProductionError.CardIssueNotFound(payload.PersonId, payload.CardClass, payload.SequenceNumber);
            }

            EnsureNoOpenJob(tenantId, payload.PersonId, payload.CardClass, payload.SequenceNumber);

            var jobSequenceNumber = repository.NextJobSequenceNumber(tenantId, payload.PersonId, payload.CardClass, payload.SequenceNumber);
            var normalisedReprintReason = ValidateReprintReason(jobSequenceNumber, payload.ReprintReason);

            var now = UtcInstant.Now();
            var packageActor = new AuditActor(ActorTypeExtensions.TryParse(payload.OperatorActorType, out var actorType) ? actorType : ActorType.Unknown, payload.OperatorActorId);
            var print = new ProductionStepResult { Passed = printPassed, Detail = printDetail, RecordedAt = now };
            var encode = new ProductionStepResult { Passed = encodePassed, Detail = encodeDetail, RecordedAt = now };
            var readBackQc = new ProductionStepResult { Passed = readBackQcPassed, Detail = readBackQcDetail, RecordedAt = now };
            var job = new ProductionJob(tenantId, payload.PersonId, payload.CardClass, payload.SequenceNumber, jobSequenceNumber)
            {
                Status = printPassed && encodePassed && readBackQcPassed ? ProductionJobStatus.Passed : ProductionJobStatus.Failed,
                Version = 1,
                PrinterRef = payload.PrinterRef,
                Operator = packageActor,
                WorkstationRef = payload.WorkstationRef,
                ReprintReason = normalisedReprintReason,
                Print = print,
                Encode = encode,
                ReadBackQc = readBackQc,
                Source = "offline",
                PackageId = payload.PackageId,
                CreatedAt = now,
                UpdatedAt = now,
            };

            await AuditAsync(tenantId, actor, "production.complete_offline_job", payload.PersonId, payload.CardClass, payload.SequenceNumber, jobSequenceNumber, "production_offline_job_completed", correlationId,
                new Dictionary<string, (object?, string)> { ["job_sequence_number"] = (jobSequenceNumber, "internal"), ["package_id"] = (payload.PackageId, "internal") },
                cancellationToken);

            var created = repository.Create(job);
            packageStore.Replace(storedPackage, storedPackage with { Consumed = true, ConsumedAt = now, Version = storedPackage.Version + 1 });

            idempotencyStore.Complete(tenantId, CompleteOfflineJobOperation, idempotencyKey, created);
            return created;
        }
        catch
        {
            idempotencyStore.Release(tenantId, CompleteOfflineJobOperation, idempotencyKey);
            throw;
        }
    }

    /// <summary>BIO-CRD-007-AC02/AC05: refuses to even attempt activation unless the latest production job for this card has Passed, then delegates to BIO-CRD-005's own real, authoritative activation.</summary>
    public async Task<CardIssueSnapshot> ActivateAfterProductionAsync(
        string personId, string cardClass, int sequenceNumber, int? expectedVersion, string idempotencyKey,
        AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw ProductionError.IdempotencyKeyRequired();
        }

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);
        using var __ = await activationBarrier.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);

        var latest = repository.GetLatest(tenantId, personId, cardClass, sequenceNumber);
        if (latest is null || !latest.Status.AllowsActivation())
        {
            throw ProductionError.ProductionNotPassed(latest?.JobSequenceNumber, latest?.Status);
        }

        return await cardIssueGateway.ActivateAsync(personId, cardClass, sequenceNumber, expectedVersion, idempotencyKey, actor, correlationId, cancellationToken);
    }

    private void EnsureNoOpenJob(string tenantId, string personId, string cardClass, int sequenceNumber)
    {
        var latest = repository.GetLatest(tenantId, personId, cardClass, sequenceNumber);
        if (latest is not null && latest.Status == ProductionJobStatus.InProgress)
        {
            throw ProductionError.JobNotOpen("create", latest.Status);
        }
    }

    private static string? ValidateReprintReason(int jobSequenceNumber, string? reprintReason)
    {
        var normalised = string.IsNullOrWhiteSpace(reprintReason) ? null : reprintReason.Trim();
        if (jobSequenceNumber > 1 && normalised is null)
        {
            throw ProductionError.ReprintReasonRequired();
        }

        if (jobSequenceNumber == 1 && normalised is not null)
        {
            throw ProductionError.ReprintReasonNotAllowed();
        }

        return normalised;
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, string cardClass, int sequenceNumber, int? jobSequenceNumber, string reasonCode,
        string correlationId, IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var targetId = jobSequenceNumber is int j ? $"{personId}:{cardClass}:{sequenceNumber}:{j}" : $"{personId}:{cardClass}:{sequenceNumber}";
        var auditEvent = ProductionAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("production_job", targetId), AuditOutcome.Allowed, reasonCode, correlationId, causationId: null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return;
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
            throw ProductionError.AuditUnavailable();
        }

        if (!exists)
        {
            throw ProductionError.AuditUnavailable();
        }
    }

    private static void RequireExpectedVersion(int? expectedVersion, int actualVersion)
    {
        if (expectedVersion is not int expected)
        {
            throw ProductionError.PreconditionRequired();
        }

        if (expected != actualVersion)
        {
            throw ProductionError.ConcurrencyConflict(expected, actualVersion);
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw ProductionError.TenantContextMissing();

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
            throw ProductionError.Validation(errors);
        }
    }
}
