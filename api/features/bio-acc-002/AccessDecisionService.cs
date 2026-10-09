using Biolog.Features.AccessDecisions.Api;
using Biolog.Features.AccessDecisions.Audit;
using Biolog.Features.AccessDecisions.Domain;
using Biolog.Features.AccessDecisions.Identifiers;
using Biolog.Features.AccessDecisions.Tenancy;

namespace Biolog.Features.AccessDecisions;

/// <summary>
/// Application service for BIO-ACC-002-AC01-AC04: orchestrates biometric evidence gathering and
/// policy evaluation, then hands both to the pure <see cref="AccessDecisionCombiner"/> (AC04's
/// determinism boundary - see that class's own header). Implements ADR-0003 Decision 7's
/// audit-atomicity pattern exactly as every sibling service in this codebase's identity wave.
///
/// <b>AC01 - evidence gathered BEFORE the policy is ever consulted.</b> Both biometric gateways
/// are called first (if a sample was supplied), the derived <see cref="AssuranceLevel"/> is
/// computed from their outputs, and ONLY THEN is <see cref="IPolicyDecisionGateway"/> called with
/// that derived value - the policy engine's own <c>Decision</c>/<c>ReasonCode</c> is the sole
/// source of ALLOW/DENY, never this service.
///
/// <b>AC02 - suspended/revoked identity/credential denies.</b> Inherited entirely from
/// BIO-ACC-001's own policy engine (see <see cref="IPolicyDecisionGateway"/>'s own header) - this
/// service does not re-check identity or credential status itself.
///
/// <b>This feature's own genuine authorization decision (ADR-0006 Decision 1/2).</b> Unlike
/// BIO-ATT-001's/BIO-TRN-002's own capture endpoints, this service's caller is required to hold a
/// verified provenance proof (checked at the endpoint layer, <see cref="Api.ActorProvenanceGate"/>)
/// before this method ever runs - the request reaches the same genuine authorization outcome
/// BIO-ACC-001's own gated <c>/evaluate</c> endpoint reaches, one hop removed.
/// </summary>
internal sealed class AccessDecisionService(
    ICurrentTenantProvider tenantProvider,
    IAccessDecisionEventRepository repository,
    IAccessDecisionAuditSink auditSink,
    AccessDecisionLockRegistry lockRegistry,
    IPersonExistenceGateway personGateway,
    IPolicyDecisionGateway policyGateway,
    IFingerprintEvidenceGateway fingerprintGateway,
    IFaceEvidenceGateway faceGateway)
{
    public async Task<AccessDecisionEvent> DecideAsync(
        DecideAccessRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var errors = new List<ValidationErrorItem>();

        if (string.IsNullOrWhiteSpace(request.PersonId) || !CanonicalId.IsPersonId(request.PersonId))
        {
            errors.Add(new ValidationErrorItem("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier"));
        }

        if (string.IsNullOrWhiteSpace(request.CredentialClass))
        {
            errors.Add(new ValidationErrorItem("credential_class", "required", "credential_class is required"));
        }

        if (string.IsNullOrWhiteSpace(request.LocationId))
        {
            errors.Add(new ValidationErrorItem("location_id", "required", "location_id is required"));
        }

        if (string.IsNullOrWhiteSpace(request.PolicyKey))
        {
            errors.Add(new ValidationErrorItem("policy_key", "required", "policy_key is required"));
        }

        if (!CardStatusExtensions.TryParse(request.PresentedCardStatus, out var presentedCardStatus))
        {
            errors.Add(new ValidationErrorItem("presented_card_status", "invalid_format", "presented_card_status must be issued, active or deactivated"));
        }

        var occurredAt = string.IsNullOrWhiteSpace(request.OccurredAt) ? UtcInstant.Now() : request.OccurredAt;
        if (!string.IsNullOrWhiteSpace(request.OccurredAt) && !UtcInstant.TryParse(request.OccurredAt, out _))
        {
            errors.Add(new ValidationErrorItem("occurred_at", "invalid_format", "occurred_at must be an RFC 3339 UTC instant if present"));
        }

        if (request.IdempotencyKey is { Length: > 200 })
        {
            errors.Add(new ValidationErrorItem("idempotency_key", "invalid", "idempotency_key must be at most 200 characters"));
        }

        if (errors.Count > 0)
        {
            throw AccessDecisionError.Validation(errors);
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey;

        IDisposable? held = null;
        if (idempotencyKey is not null)
        {
            held = await lockRegistry.AcquireAsync(tenantId, idempotencyKey, cancellationToken);
        }

        try
        {
            if (idempotencyKey is not null)
            {
                var existing = await repository.GetByIdempotencyKeyAsync(tenantId, idempotencyKey, cancellationToken);
                if (existing is not null)
                {
                    // AC02-style idempotent redelivery: the same physical presentation resent over
                    // the network is answered with the SAME already-recorded decision, never
                    // re-run through the biometric gateways a second time (each of which has its
                    // own side-effecting verification-attempt record on the sibling side).
                    return existing;
                }
            }

            if (!await personGateway.ExistsAsync(request.PersonId!, cancellationToken))
            {
                throw AccessDecisionError.PersonNotFound(request.PersonId!);
            }

            var evidence = new List<BiometricEvidenceEntry>();
            if (request.FingerprintSample is not null)
            {
                var sample = DecodeSample(request.FingerprintSample, "fingerprint_sample");
                evidence.Add(await fingerprintGateway.VerifyAsync(request.PersonId!, sample, cancellationToken));
            }

            if (request.FaceSample is not null)
            {
                var sample = DecodeSample(request.FaceSample, "face_sample");
                evidence.Add(await faceGateway.VerifyAsync(request.PersonId!, sample, cancellationToken));
            }

            var presentedAssuranceLevel = AccessDecisionCombiner.DeriveAssuranceLevel(evidence);

            var query = new PolicyDecisionQuery
            {
                PolicyKey = request.PolicyKey!,
                PersonId = request.PersonId!,
                ProfileKey = string.IsNullOrWhiteSpace(request.ProfileKey) ? null : request.ProfileKey.Trim(),
                CredentialClass = request.CredentialClass!.Trim(),
                LocationId = request.LocationId!.Trim(),
                OccurredAtUtc = occurredAt!,
                PresentedCardStatus = presentedCardStatus.ToWireString(),
                PresentedAssuranceLevel = presentedAssuranceLevel.ToWireString(),
            };
            var policyResult = await policyGateway.EvaluateAsync(query, actor, correlationId, cancellationToken);

            var decision = AccessDecisionCombiner.Combine(
                CanonicalId.NewEventId(), tenantId, request.PersonId!, query.ProfileKey, query.CredentialClass, query.LocationId,
                presentedCardStatus, evidence, policyResult, occurredAt!, UtcInstant.Now(), correlationId, idempotencyKey);

            await AuditAsync(tenantId, actor, decision, correlationId, cancellationToken);

            return await repository.CreateAsync(decision, CancellationToken.None);
        }
        finally
        {
            held?.Dispose();
        }
    }

    public async Task<IReadOnlyList<AccessDecisionEvent>> ListForPersonAsync(string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.ListForPersonAsync(tenantId, personId, cancellationToken);
    }

    public async Task<IReadOnlyList<AccessDecisionEvent>> ListForTenantAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.ListForTenantAsync(tenantId, cancellationToken);
    }

    private static Domain.BiometricSampleInput DecodeSample(BiometricSampleRequest sample, string fieldPrefix)
    {
        if (string.IsNullOrWhiteSpace(sample.PayloadBase64) || string.IsNullOrWhiteSpace(sample.ContentType) || string.IsNullOrWhiteSpace(sample.CapturedAt))
        {
            throw AccessDecisionError.Validation([new ValidationErrorItem(fieldPrefix, "required", $"{fieldPrefix} requires payload_base64, content_type and captured_at")]);
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(sample.PayloadBase64);
        }
        catch (FormatException)
        {
            throw AccessDecisionError.Validation([new ValidationErrorItem($"{fieldPrefix}.payload_base64", "invalid_format", "payload_base64 must be valid base64")]);
        }

        return new Domain.BiometricSampleInput { Payload = payload, ContentType = sample.ContentType, CapturedAtUtc = sample.CapturedAt };
    }

    private async Task AuditAsync(string tenantId, AuditActor actor, AccessDecisionEvent decision, string correlationId, CancellationToken cancellationToken)
    {
        var outcome = decision.Decision == AccessDecisionOutcome.Allow ? AuditOutcome.Allowed : AuditOutcome.Denied;
        var detail = new Dictionary<string, (object?, string)>
        {
            ["policy_key"] = (decision.PolicyKey, "internal"),
            ["policy_version_number"] = (decision.PolicyVersionNumber, "internal"),
            ["matched_rule_id"] = (decision.MatchedRuleId, "internal"),
            ["credential_class"] = (decision.CredentialClass, "internal"),
            ["location_id"] = (decision.LocationId, "internal"),
            ["subject_person_id"] = (decision.PersonId, "internal"),
            ["presented_assurance_level"] = (decision.PresentedAssuranceLevel.ToWireString(), "internal"),
        };
        var auditEvent = AccessDecisionAuditEventBuilder.Build(
            tenantId, actor, "access_decision.computed", new AuditTarget("access_decision_event", decision.EventId), outcome, decision.ReasonCode, correlationId, null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw AccessDecisionError.AuditUnavailable();
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw AccessDecisionError.TenantContextMissing();
}
