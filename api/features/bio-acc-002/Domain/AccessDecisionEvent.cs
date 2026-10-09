using Biolog.Features.AccessDecisions.Identifiers;

namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>
/// One immutable, append-only computed access decision (AC01-AC04) - a historical fact, never
/// mutated or superseded, the same "an event is a fact" discipline BIO-ATT-001's own
/// <c>AttendanceEvent</c>/BIO-TRN-002's own <c>TransportEvent</c> apply, adapted to this story's
/// own shape.
///
/// <b>AC01 - <see cref="BiometricEvidence"/> vs <see cref="Decision"/>.</b> The evidence list is
/// exactly what each biometric gateway reported, unmodified; <see cref="Decision"/>/
/// <see cref="ReasonCode"/>/<see cref="PolicyVersionNumber"/>/<see cref="MatchedRuleId"/> are
/// exactly what BIO-ACC-001's own policy engine returned. Nothing in this feature ever
/// overrides either side.
///
/// <b>AC02.</b> <see cref="Decision"/>/<see cref="ReasonCode"/> already encode BIO-ACC-001's own
/// suspended/revoked deny-precedence (this feature does not re-check identity/credential status
/// itself - see <c>IPolicyDecisionGateway</c>'s own header).
///
/// <b>AC03.</b> <see cref="PolicyKey"/>/<see cref="PolicyVersionNumber"/>/<see cref="ReasonCode"/>
/// are always populated (a <c>DENY</c> for "no published policy" still names the policy key it
/// looked for).
/// </summary>
public sealed record AccessDecisionEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public string? ProfileKey { get; init; }
    public required string CredentialClass { get; init; }
    public required string LocationId { get; init; }
    public required CardStatus PresentedCardStatus { get; init; }

    /// <summary>AC01's derived value (<see cref="AccessDecisionCombiner.DeriveAssuranceLevel"/>) - never caller-supplied.</summary>
    public required AssuranceLevel PresentedAssuranceLevel { get; init; }

    /// <summary>AC01 - the raw biometric evidence this decision was based on, unmodified. May be empty (a card-only presentation is a legitimate Basic-assurance request).</summary>
    public required IReadOnlyList<BiometricEvidenceEntry> BiometricEvidence { get; init; }

    public required string PolicyKey { get; init; }
    public required AccessDecisionOutcome Decision { get; init; }
    public required string ReasonCode { get; init; }
    public int? PolicyVersionNumber { get; init; }
    public int? MatchedRuleId { get; init; }

    /// <summary>When the presentation actually happened, per the caller's clock.</summary>
    public required string OccurredAt { get; init; }

    /// <summary>When this server computed the decision - server-stamped, excluded from idempotency comparison.</summary>
    public required string RecordedAt { get; init; }

    public required string CorrelationId { get; init; }

    /// <summary>AC02/AC04 - a caller-minted retry key. Null is valid (a one-shot decision request); when present, a reused key with identical content is answered idempotently (see <see cref="AccessDecisionService"/>).</summary>
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// AC04's determinism boundary: everything this feature computes FROM gathered inputs (evidence,
/// the policy gateway's own result, and the caller's request fields) is this one pure function -
/// no I/O, no clock read, no randomness. Given the same evidence list, the same
/// <see cref="PolicyDecisionResult"/> and the same request fields/eventId/timestamps, it always
/// returns a structurally identical <see cref="AccessDecisionEvent"/>. A caller replays a decision
/// deterministically in a test by feeding it fixed fake-gateway outputs; the async orchestration in
/// <see cref="AccessDecisionService"/> is the only part that ever touches a gateway or a clock.
/// </summary>
public static class AccessDecisionCombiner
{
    /// <summary>
    /// AC01: the ONLY place this feature turns biometric evidence into an assurance signal, and
    /// it never itself decides allow/deny - it only feeds BIO-ACC-001's own policy engine a
    /// stronger or weaker input. No evidence at all (a card-only presentation) is Basic, not an
    /// error - a policy that requires more than Basic will itself deny it (via
    /// <c>insufficient_assurance</c>), the same "let the policy decide" discipline this feature
    /// applies everywhere else. Two independently MATCHED modalities raise assurance to High;
    /// exactly one raises it to Elevated; a NotMatched/AcquisitionFailed/ReferenceStale/NotEnrolled
    /// entry never raises assurance on its own (it simply fails to elevate - the policy engine, not
    /// this function, is what turns "assurance too low" into a deny).
    /// </summary>
    public static AssuranceLevel DeriveAssuranceLevel(IReadOnlyList<BiometricEvidenceEntry> evidence)
    {
        var matchedCount = evidence.Count(e => e.ComparisonOutcome == BiometricComparisonOutcome.Matched);
        return matchedCount switch
        {
            >= 2 => AssuranceLevel.High,
            1 => AssuranceLevel.Elevated,
            _ => AssuranceLevel.Basic,
        };
    }

    public static AccessDecisionEvent Combine(
        string eventId,
        string tenantId,
        string personId,
        string? profileKey,
        string credentialClass,
        string locationId,
        CardStatus presentedCardStatus,
        IReadOnlyList<BiometricEvidenceEntry> biometricEvidence,
        PolicyDecisionResult policyResult,
        string occurredAt,
        string recordedAt,
        string correlationId,
        string? idempotencyKey)
    {
        return new AccessDecisionEvent
        {
            EventId = eventId,
            TenantId = tenantId,
            PersonId = personId,
            ProfileKey = profileKey,
            CredentialClass = credentialClass,
            LocationId = locationId,
            PresentedCardStatus = presentedCardStatus,
            PresentedAssuranceLevel = DeriveAssuranceLevel(biometricEvidence),
            BiometricEvidence = biometricEvidence,
            PolicyKey = policyResult.PolicyKey,
            Decision = policyResult.Decision,
            ReasonCode = policyResult.ReasonCode,
            PolicyVersionNumber = policyResult.PolicyVersionNumber,
            MatchedRuleId = policyResult.MatchedRuleId,
            OccurredAt = occurredAt,
            RecordedAt = recordedAt,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey,
        };
    }

    /// <summary>SHA-256 over every business field a caller controls, EXCLUDING <see cref="AccessDecisionEvent.EventId"/>/<see cref="AccessDecisionEvent.RecordedAt"/>/<see cref="AccessDecisionEvent.CorrelationId"/> (all per-delivery, never per-content) - the same exclusion BIO-ATT-001's/BIO-TRN-002's own fingerprints make, for the identical reason.</summary>
    public static string ComputeFingerprint(
        string tenantId, string personId, string? profileKey, string credentialClass, string locationId,
        CardStatus presentedCardStatus, IReadOnlyList<BiometricEvidenceEntry> evidence, string policyKey, string occurredAt)
    {
        var projection = new
        {
            tenant_id = tenantId,
            person_id = personId,
            profile_key = profileKey,
            credential_class = credentialClass,
            location_id = locationId,
            presented_card_status = presentedCardStatus.ToWireString(),
            evidence = evidence.Select(e => new { modality = e.Modality.ToWireString(), outcome = e.ComparisonOutcome.ToWireString(), score = e.Score }).ToArray(),
            policy_key = policyKey,
            occurred_at = occurredAt,
        };
        var canonicalJson = System.Text.Json.JsonSerializer.Serialize(projection);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonicalJson));
        return Convert.ToHexStringLower(hash);
    }
}
