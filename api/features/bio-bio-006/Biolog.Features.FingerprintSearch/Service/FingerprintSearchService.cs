using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FingerprintSearch.Domain;

namespace Biolog.Features.FingerprintSearch.Service;

/// <summary>
/// BIO-BIO-006: 1:N fingerprint search for duplicate detection/identification. Reuses
/// BIO-BIO-001's own <see cref="IFingerprintProvider"/>/<see cref="IBiometricMatcher"/> seams
/// directly, and searches against this feature's own <see cref="IFingerprintGalleryStore"/>
/// (never BIO-BIO-001's per-identity <c>IBiometricTemplateStore</c>, which cannot
/// enumerate a tenant's population - see this project's csproj remarks).
///
/// <b>AC01 - "search returns ranked candidates and scores".</b> <see cref="SearchAsync"/> scores
/// the live probe against every OTHER gallery-indexed identity in the resolved tenant, orders the
/// results by score descending, and caps the list at the caller-requested candidate count - see
/// <see cref="FingerprintSearchResult"/>.
///
/// <b>AC02 - "permissions distinguish 1:N from 1:1".</b> <see cref="SearchAsync"/> requires the
/// calling actor to hold <see cref="SearchPermissionKey"/>, verified through
/// <see cref="IBiometricSearchPermissionGateway"/> BEFORE the gallery is even read - a genuinely
/// separate permission key from whatever BIO-BIO-004's own 1:1 verification might one day
/// require (that story enforces none today), so holding one grant never implies the other.
///
/// <b>AC03 - "possible duplicate routes to human review".</b> EVERY compared candidate whose
/// score meets <see cref="IFingerprintDuplicateThresholdPolicy.GetThreshold"/> is
/// raised/augmented onto this feature's own <see cref="FingerprintDuplicateReviewEntry"/> queue
/// (<see cref="RaiseOrAugmentReviewAsync"/>) - the same "signals accumulate, a decided pair is
/// never silently reopened" discipline BIO-IDN-005's own duplicate-review aggregate established,
/// mirrored here rather than reused directly because BIO-IDN-005's own raise/augment method is
/// <c>private</c> with no public entry point this story can reach (see csproj remarks).
///
/// <b>BIO-BIO-006-QA-01 (Seyi round 1, High).</b> Review routing is computed from EVERY scored
/// candidate, BEFORE AC01's own <c>maxCandidates</c> cap is applied to the RETURNED ranked list -
/// those are two independent concerns. An earlier version routed only the candidates that
/// survived the cap, so an above-threshold candidate crowded out of the top
/// <c>maxCandidates</c> (e.g. a caller asking for only the single best match while several
/// candidates score above the duplicate threshold) never reached human review at all - a
/// possible duplicate silently never routed anywhere, exactly what AC03 exists to prevent.
///
/// <b>BIO-BIO-006-QA-02 (Chioma acceptance, High).</b> <see cref="RaiseOrAugmentReviewAsync"/>'s
/// read-check-write is now serialized per pair via <see cref="FingerprintDuplicateReviewLockRegistry"/>
/// (mirrored for <see cref="DecideAsync"/>'s own read-check(version)-write). An earlier version
/// held no lock, so two simultaneous searches that both first observed the SAME (subject,
/// candidate) pair as a new possible duplicate both read "no existing entry" and both attempted
/// to create one - the repository's own uniqueness check correctly rejected the second write,
/// but nothing converged the two callers onto the one successfully-raised entry, so one of two
/// entirely legitimate concurrent searches failed with an unhandled exception instead.
///
/// <b>AC04 - "no automatic PERSON merge occurs".</b> This project has no <c>ProjectReference</c>
/// to BIO-IDN-001 at all, so it structurally cannot call into <c>PersonService</c> to merge,
/// archive or delete anything. <see cref="DecideAsync"/> only ever records a human's decision on
/// THIS feature's own <see cref="FingerprintDuplicateReviewEntry"/> record - see
/// docs/features/bio-bio-006/README.md "AC04 - no merge exists in this codebase".
///
/// <b>AC05 - "search scope is tenant constrained".</b> <see cref="IFingerprintGalleryStore.GetAllForTenantAsync"/>
/// is keyed by the resolved <paramref name="tenantId"/> alone; this method accepts no parameter,
/// header or flag that would widen a search beyond it - the same fail-closed rule BIO-IDN-005's
/// own duplicate scan documents for itself.
///
/// <b>Provider extraction failures.</b> Unlike BIO-BIO-004/005's own verification services, this
/// story does not catch and record a typed "acquisition failed" outcome for a live sample that
/// fails to extract - no AC here asks for a persisted per-attempt evidence record the way AC01/
/// AC02 of BIO-BIO-004/005 do, and a search over a population is a fundamentally different shape
/// of operation from a single 1:1 decision. An extraction failure (e.g.
/// <see cref="ArgumentException"/> for an empty/unusable live sample) is treated as a genuine
/// caller/integration error and propagates - a deliberate scope decision, not an oversight, see
/// docs/features/bio-bio-006/README.md.
///
/// <b>Internal, like every prior BIO-BIO story's own service.</b> This service depends on the
/// internal <see cref="IFingerprintGalleryStore"/>/<see cref="IFingerprintDuplicateReviewRepository"/>,
/// so it is internal too - reachable only from this assembly, <c>Biolog.Api</c> and this
/// feature's own test project.
///
/// <b>Tenant context.</b> Like every prior BIO-BIO story's service, this takes a caller-resolved
/// tenant/person id rather than resolving one itself; this project defines no tenant-context seam
/// of its own.
/// </summary>
internal sealed class FingerprintSearchService(
    IFingerprintGalleryStore galleryStore,
    IFingerprintProvider fingerprintProvider,
    IBiometricMatcher matcher,
    IFingerprintDuplicateThresholdPolicy thresholdPolicy,
    IBiometricSearchPermissionGateway permissionGateway,
    IFingerprintDuplicateReviewRepository reviewRepository,
    FingerprintDuplicateReviewLockRegistry lockRegistry,
    Func<string>? newId = null,
    Func<string>? nowUtc = null)
{
    /// <summary>AC02: the permission an actor must hold to perform a 1:N search - distinct from
    /// any permission a 1:1 verification workflow might require.</summary>
    public const string SearchPermissionKey = "biometric.fingerprint_search_1n";

    private readonly Func<string> _newId = newId ?? (() => Guid.NewGuid().ToString());
    private readonly Func<string> _nowUtc = nowUtc ?? (() => DateTime.UtcNow.ToString("O"));

    /// <summary>The explicit gallery-registration step - see <see cref="IFingerprintGalleryStore"/>'s
    /// remarks for why this is caller-driven rather than automatic.</summary>
    public async Task IndexAsync(string tenantId, string personId, BiometricSample sample, CancellationToken cancellationToken)
    {
        var now = _nowUtc();
        var features = await fingerprintProvider.ExtractTemplateAsync(sample, cancellationToken).ConfigureAwait(false);
        var template = BiometricTemplate.Create(
            _newId(), tenantId, personId, BiometricModality.Fingerprint, fingerprintProvider.ProviderId, features.ProviderVersion,
            features.Payload, now);
        await galleryStore.IndexAsync(template, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>BIO-BIO-009-AC02/AC04: a plain persistence wrapper, matching
    /// <see cref="IndexAsync"/>'s own directness - no lock, no audit (the caller, BIO-BIO-009's
    /// own gateway, owns the full ADR-0003 Decision 7 discipline for this operation).</summary>
    public Task<bool> RemoveFromGalleryAsync(string tenantId, string personId, CancellationToken cancellationToken) =>
        galleryStore.RemoveAsync(tenantId, personId, cancellationToken);

    public async Task<FingerprintSearchResult> SearchAsync(
        string tenantId, string subjectPersonId, string actorId, BiometricSample sample, int maxCandidates, CancellationToken cancellationToken)
    {
        if (maxCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCandidates), maxCandidates, "maxCandidates must be positive.");
        }

        if (!await permissionGateway.HasPermissionAsync(tenantId, actorId, SearchPermissionKey, cancellationToken).ConfigureAwait(false))
        {
            throw FingerprintSearchException.PermissionRequired(SearchPermissionKey);
        }

        var gallery = await galleryStore.GetAllForTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var now = _nowUtc();
        var features = await fingerprintProvider.ExtractTemplateAsync(sample, cancellationToken).ConfigureAwait(false);
        var probe = BiometricTemplate.Create(
            _newId(), tenantId, subjectPersonId, BiometricModality.Fingerprint, fingerprintProvider.ProviderId, features.ProviderVersion,
            features.Payload, now);

        var threshold = thresholdPolicy.GetThreshold(tenantId);

        var scored = new List<(string PersonId, double Score)>();
        foreach (var candidate in gallery)
        {
            if (candidate.PersonId == subjectPersonId)
            {
                continue; // never match the subject against their own gallery entry
            }

            var matchResult = await matcher.CompareAsync(probe, candidate, cancellationToken).ConfigureAwait(false);
            scored.Add((candidate.PersonId, matchResult.Score));
        }

        // BIO-BIO-006-QA-01 (Seyi round 1, High): AC03's review-routing must apply to EVERY
        // candidate that meets the threshold, not only the ones that survive AC01's own
        // maxCandidates cap on the RETURNED ranked list - those are two independent concerns
        // (what the caller sees vs. what gets escalated for human review). Routing is computed
        // from the full, uncapped `scored` set, before `Take(maxCandidates)` ever runs.
        foreach (var (personId, score) in scored)
        {
            if (score >= threshold)
            {
                await RaiseOrAugmentReviewAsync(tenantId, subjectPersonId, personId, score, cancellationToken).ConfigureAwait(false);
            }
        }

        var ranked = scored
            .OrderByDescending(s => s.Score)
            .Take(maxCandidates)
            .Select(s => new FingerprintSearchCandidate { PersonId = s.PersonId, Score = s.Score, PossibleDuplicate = s.Score >= threshold })
            .ToList();

        return new FingerprintSearchResult
        {
            SearchId = _newId(),
            TenantId = tenantId,
            SubjectPersonId = subjectPersonId,
            RankedCandidates = ranked,
            ProviderId = fingerprintProvider.ProviderId,
            ProviderVersion = features.ProviderVersion,
            ThresholdUsed = threshold,
            OccurredAtUtc = now,
        };
    }

    /// <summary>AC03's review queue: every entry raised for this tenant, pending or decided.</summary>
    public Task<IReadOnlyList<FingerprintDuplicateReviewEntry>> ListReviewQueueAsync(string tenantId, CancellationToken cancellationToken) =>
        reviewRepository.ListForTenantAsync(tenantId, cancellationToken);

    /// <summary>AC03/AC04: records a human review decision. Never creates, deletes or touches a
    /// PERSON record - see class header.</summary>
    public async Task<FingerprintDuplicateReviewEntry> DecideAsync(
        string tenantId, string personIdA, string personIdB, FingerprintDuplicateReviewStatus decision, string actorId, int expectedVersion,
        CancellationToken cancellationToken)
    {
        if (decision != FingerprintDuplicateReviewStatus.ConfirmedDuplicate && decision != FingerprintDuplicateReviewStatus.ConfirmedDistinct)
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "decision must be ConfirmedDuplicate or ConfirmedDistinct.");
        }

        var (personIdLow, personIdHigh) = FingerprintDuplicateReviewEntry.OrderPair(personIdA, personIdB);

        using var _ = await lockRegistry.AcquireAsync(tenantId, personIdLow, personIdHigh, cancellationToken).ConfigureAwait(false);
        var current = await reviewRepository.GetAsync(tenantId, personIdLow, personIdHigh, cancellationToken).ConfigureAwait(false)
            ?? throw FingerprintSearchException.ReviewEntryNotFound(personIdLow, personIdHigh);

        if (expectedVersion != current.Version)
        {
            throw FingerprintSearchException.ConcurrencyConflict(personIdLow, personIdHigh, expectedVersion, current.Version);
        }

        var now = _nowUtc();
        var updated = current with
        {
            Status = decision,
            DecidedAtUtc = now,
            DecidedByActorId = actorId,
            Version = current.Version + 1,
            UpdatedAtUtc = now,
        };

        return await reviewRepository.ReplaceAsync(current, updated, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Mirrors BIO-IDN-005's own raise/augment discipline (see class header): a new pair starts
    /// <see cref="FingerprintDuplicateReviewStatus.PendingReview"/>; a pair a human already
    /// decided is left untouched even if a later search re-observes it; an already-pending pair's
    /// <see cref="FingerprintDuplicateReviewEntry.Score"/> is raised to the higher of the two
    /// observations, never lowered.
    /// </summary>
    private async Task RaiseOrAugmentReviewAsync(
        string tenantId, string personIdA, string personIdB, double score, CancellationToken cancellationToken)
    {
        var (personIdLow, personIdHigh) = FingerprintDuplicateReviewEntry.OrderPair(personIdA, personIdB);

        // BIO-BIO-006-QA-02 (Chioma acceptance, High): the read-check-write below is not atomic
        // on its own - two concurrent searches that both first observe this pair as a new
        // possible duplicate would both read `existing is null` and both attempt to create,
        // and the repository's own uniqueness check would reject the second write with an
        // unhandled exception instead of converging. Holding the per-pair lock across the whole
        // sequence serializes only operations on THIS pair; every other pair still proceeds in
        // parallel (see FingerprintDuplicateReviewLockRegistry's own remarks).
        using var _ = await lockRegistry.AcquireAsync(tenantId, personIdLow, personIdHigh, cancellationToken).ConfigureAwait(false);
        var existing = await reviewRepository.GetAsync(tenantId, personIdLow, personIdHigh, cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            var now = _nowUtc();
            var entry = new FingerprintDuplicateReviewEntry(tenantId, personIdLow, personIdHigh)
            {
                Score = score,
                Status = FingerprintDuplicateReviewStatus.PendingReview,
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            await reviewRepository.CreateAsync(entry, CancellationToken.None).ConfigureAwait(false);
            return;
        }

        if (existing.Status != FingerprintDuplicateReviewStatus.PendingReview)
        {
            // A human already reviewed this pair - a routine re-search must never silently
            // reopen or relitigate that call.
            return;
        }

        if (score <= existing.Score)
        {
            // Nothing new to record.
            return;
        }

        var updated = existing with { Score = score, UpdatedAtUtc = _nowUtc(), Version = existing.Version + 1 };
        await reviewRepository.ReplaceAsync(existing, updated, CancellationToken.None).ConfigureAwait(false);
    }
}
