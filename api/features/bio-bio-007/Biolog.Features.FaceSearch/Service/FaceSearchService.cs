using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FaceSearch.Audit;
using Biolog.Features.FaceSearch.Domain;

namespace Biolog.Features.FaceSearch.Service;

/// <summary>
/// BIO-BIO-007: 1:N face identification/duplicate search against an APPROVED tenant population.
/// Reuses BIO-BIO-001's own <see cref="IFaceProvider"/>/<see cref="IBiometricMatcher"/> seams
/// directly for the live probe, and searches against this feature's own <see cref="IFaceGalleryStore"/>
/// (never BIO-BIO-003's per-identity approved reference store directly, which cannot enumerate a
/// tenant's population - see this project's csproj remarks). Mirrors BIO-BIO-006's own
/// <c>FingerprintSearchService</c> shape for the face modality, adjusted for this story's own
/// distinct AC02/AC03/AC05 wording.
///
/// <b>BIO-BIO-007-QA-01 (Chioma acceptance, High).</b> The user story's own wording is "search
/// against APPROVED tenant populations," not merely "search against whatever a caller submits."
/// An earlier version of <see cref="IndexAsync"/> accepted a caller-supplied raw
/// <see cref="BiometricSample"/>, extracted a template directly through <see cref="IFaceProvider"/>
/// and indexed it with no check of capture quality, liveness, or BIO-BIO-003 approval - an
/// acceptance probe correctly showed this let an unapproved or failed-liveness capture (or
/// outright arbitrary bytes) enter the population every search result and audit event labelled
/// "approved." <see cref="IndexAsync"/> now takes no sample at all: it resolves the claimed
/// identity's CURRENT approved reference through <see cref="IFaceGalleryApprovalGateway"/> (a
/// narrow gateway onto BIO-IDN-002-QA-01's established pattern, real adapter wrapping
/// BIO-BIO-003's own already-registered <c>FaceEnrolmentService.GetLatestReferenceAsync</c>) and
/// indexes THAT reference's own already-vetted template - there is no code path left in this
/// feature that can place an unapproved template into the gallery.
///
/// <b>AC01 - "search returns ranked candidates with similarity".</b> <see cref="SearchAsync"/>
/// scores the live probe against every OTHER gallery-indexed identity in the resolved tenant,
/// orders the results by score descending, and caps the list at the caller-requested candidate
/// count - see <see cref="FaceSearchResult"/>.
///
/// <b>AC02 - "1:N requires explicit tenant feature enablement".</b> <see cref="SearchAsync"/>
/// checks <see cref="IFaceSearchFeatureFlagPolicy.IsEnabledForTenant"/> BEFORE the gallery is
/// even read. This is a TENANT-WIDE switch, not a per-actor permission (the same distinction
/// BIO-BIO-006-AC02's own permission gate draws in the opposite direction) - a tenant that has
/// never explicitly enabled 1:N face search can never reach a result, no matter who calls it.
///
/// <b>AC03 - "no automatic authorization is granted from search alone".</b> Neither
/// <see cref="FaceSearchResult"/> nor <see cref="FaceSearchCandidate"/> carries an allow/deny or
/// authorization field of any kind, and this class has no "decide"/"authorize" method whatsoever
/// - a search is PURELY informational. Unlike BIO-BIO-005's own evidence/decision split (where a
/// SEPARATE decision step exists, just architecturally distinct from evidence), this story's
/// AC03 is stronger: no decision step exists here AT ALL. A caller that wants to act on a search
/// result does so through a wholly separate authorization mechanism this feature has no
/// knowledge of.
///
/// <b>AC04 - "no automatic PERSON merge occurs".</b> This project has no <c>ProjectReference</c>
/// to BIO-IDN-001 at all, so it structurally cannot call into <c>PersonService</c> to merge,
/// archive or delete anything. This story defines no duplicate-review queue of its own (unlike
/// BIO-BIO-006's own AC03) - none of this story's five ACs ask for one; see
/// docs/features/bio-bio-007/README.md "Design decisions" for the explicit scope reasoning.
///
/// <b>AC05 - "population scope and audit are explicit".</b> <see cref="IFaceGalleryStore.GetAllForTenantAsync"/>
/// is keyed by the resolved tenant alone (never a parameter that could widen a search beyond
/// it); <see cref="FaceSearchResult.PopulationScope"/>/<see cref="FaceSearchResult.PopulationSize"/>
/// name exactly what was searched, visibly, on the result itself. Every call also writes a
/// <see cref="FaceSearchAuditEvent"/> via <see cref="IFaceSearchAuditSink"/> - unlike BIO-BIO-003/006's
/// own deliberate "no separate audit sink" decisions (neither story's ACs required one), THIS
/// story's AC05 explicitly requires audit, so an audit write whose durability cannot be confirmed
/// is treated as a genuine failure (<see cref="FaceSearchException.AuditUnavailable"/>) - the
/// same ADR-0003 Decision 7 discipline every mutating service in this codebase applies, adapted
/// here for a read operation whose own explicit audit trail IS the thing AC05 asks for.
///
/// <b>Provider extraction failures.</b> Like BIO-BIO-006's own sibling search story, this service
/// does not catch and record a typed "acquisition failed" outcome for a live sample that fails to
/// extract - no AC here asks for a persisted per-attempt evidence record distinguishing that from
/// a routine no-match outcome. An extraction failure (e.g. <see cref="ArgumentException"/> for an
/// empty/unusable live sample) is treated as a genuine caller/integration error and propagates -
/// a deliberate scope decision, not an oversight.
///
/// <b>Internal, like every prior BIO-BIO story's own service.</b> This service depends on the
/// internal <see cref="IFaceGalleryStore"/>/<see cref="IFaceSearchAuditSink"/>, so it is internal
/// too - reachable only from this assembly, <c>Biolog.Api</c> and this feature's own test
/// project.
///
/// <b>Tenant context.</b> Like every prior BIO-BIO story's service, this takes a caller-resolved
/// tenant/person id rather than resolving one itself; this project defines no tenant-context seam
/// of its own.
/// </summary>
internal sealed class FaceSearchService(
    IFaceGalleryStore galleryStore,
    IFaceGalleryApprovalGateway approvalGateway,
    IFaceProvider faceProvider,
    IBiometricMatcher matcher,
    IFaceSearchFeatureFlagPolicy featureFlagPolicy,
    IFaceSearchAuditSink auditSink,
    Func<string>? newId = null,
    Func<string>? nowUtc = null)
{
    /// <summary>AC05's population-scope descriptor: every search against a tenant's gallery runs
    /// against the SAME, single, explicitly-named population - "every identity currently indexed
    /// for this tenant" - never an implicit or partial default.</summary>
    public const string PopulationScope = "tenant_gallery";

    private readonly Func<string> _newId = newId ?? (() => Guid.NewGuid().ToString());
    private readonly Func<string> _nowUtc = nowUtc ?? (() => DateTime.UtcNow.ToString("O"));

    /// <summary>The explicit gallery-registration step - see <see cref="IFaceGalleryStore"/>'s
    /// remarks for why this is caller-driven rather than automatic. BIO-BIO-007-QA-01: takes no
    /// raw sample - the only way to enter the gallery is through the claimed identity's CURRENT
    /// BIO-BIO-003-approved reference, resolved via <see cref="IFaceGalleryApprovalGateway"/>.
    /// A person with no approved reference is a genuine precondition failure
    /// (<see cref="FaceSearchException.NoApprovedReference"/>), not a routine outcome.</summary>
    public async Task IndexAsync(string tenantId, string personId, CancellationToken cancellationToken)
    {
        var template = await approvalGateway.GetLatestApprovedReferenceAsync(tenantId, personId, cancellationToken).ConfigureAwait(false)
            ?? throw FaceSearchException.NoApprovedReference(personId);
        await galleryStore.IndexAsync(template, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>BIO-BIO-009-AC02/AC04: a plain persistence wrapper, matching
    /// <see cref="IndexAsync"/>'s own directness - no lock, no audit (the caller, BIO-BIO-009's
    /// own gateway, owns the full ADR-0003 Decision 7 discipline for this operation).</summary>
    public Task<bool> RemoveFromGalleryAsync(string tenantId, string personId, CancellationToken cancellationToken) =>
        galleryStore.RemoveAsync(tenantId, personId, cancellationToken);

    public async Task<FaceSearchResult> SearchAsync(
        string tenantId, string subjectPersonId, string actorId, BiometricSample sample, int maxCandidates, CancellationToken cancellationToken)
    {
        if (maxCandidates <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCandidates), maxCandidates, "maxCandidates must be positive.");
        }

        if (!featureFlagPolicy.IsEnabledForTenant(tenantId))
        {
            throw FaceSearchException.FeatureNotEnabledForTenant(tenantId);
        }

        var gallery = await galleryStore.GetAllForTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var now = _nowUtc();
        var features = await faceProvider.ExtractTemplateAsync(sample, cancellationToken).ConfigureAwait(false);
        var probe = BiometricTemplate.Create(
            _newId(), tenantId, subjectPersonId, BiometricModality.Face, faceProvider.ProviderId, features.ProviderVersion,
            features.Payload, now);

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

        var ranked = scored
            .OrderByDescending(s => s.Score)
            .Take(maxCandidates)
            .Select(s => new FaceSearchCandidate { PersonId = s.PersonId, Score = s.Score })
            .ToList();

        var searchId = _newId();
        var result = new FaceSearchResult
        {
            SearchId = searchId,
            TenantId = tenantId,
            SubjectPersonId = subjectPersonId,
            RankedCandidates = ranked,
            PopulationScope = PopulationScope,
            PopulationSize = gallery.Count,
            ProviderId = faceProvider.ProviderId,
            ProviderVersion = features.ProviderVersion,
            OccurredAtUtc = now,
        };

        await AuditAsync(searchId, tenantId, actorId, subjectPersonId, gallery.Count, ranked.Count, now, cancellationToken).ConfigureAwait(false);

        return result;
    }

    public Task<IReadOnlyList<FaceSearchAuditEvent>> ListAuditTrailAsync(string tenantId, CancellationToken cancellationToken) =>
        auditSink.ListForTenantAsync(tenantId, cancellationToken);

    /// <summary>ADR-0003 Decision 7, element 4: resolves an ambiguous <see cref="IFaceSearchAuditSink.WriteAsync"/>
    /// throw via <see cref="IFaceSearchAuditSink.ExistsAsync"/> rather than assuming failure -
    /// AC05 requires this search's audit trail to be explicit, so a write whose durability
    /// genuinely cannot be confirmed fails the whole search rather than returning a result this
    /// story cannot honestly claim was audited.</summary>
    private async Task AuditAsync(
        string searchId, string tenantId, string actorId, string subjectPersonId, int populationSize, int candidateCount, string nowUtc,
        CancellationToken cancellationToken)
    {
        var eventId = _newId();
        var auditEvent = new FaceSearchAuditEvent
        {
            EventId = eventId,
            SearchId = searchId,
            TenantId = tenantId,
            ActorId = actorId,
            SubjectPersonId = subjectPersonId,
            PopulationScope = PopulationScope,
            PopulationSize = populationSize,
            CandidateCount = candidateCount,
            OccurredAtUtc = nowUtc,
        };

        try
        {
            await auditSink.WriteAsync(auditEvent, CancellationToken.None).ConfigureAwait(false);
            return; // confirmed: the write completed without throwing
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(eventId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            throw FaceSearchException.AuditUnavailable();
        }

        if (!exists)
        {
            throw FaceSearchException.AuditUnavailable();
        }
    }
}
