using Biolog.Features.MobileVerification.Api;
using Biolog.Features.MobileVerification.Audit;
using Biolog.Features.MobileVerification.Domain;
using Biolog.Features.MobileVerification.Identifiers;

namespace Biolog.Features.MobileVerification;

/// <summary>
/// Application service for BIO-MOB-004's server side. AC01 (sanitised decision shape - enforced
/// structurally by <see cref="VerificationDecisionResult"/>'s own fields, not by this service),
/// AC02 is entirely the mobile client's own concern (nothing travels or is cached server-side
/// that this service must additionally encrypt), AC03 (device-role gate on both mobile-facing
/// actions, and the human-actor-RBAC-gated role assignment write) and AC04 (idempotent decision
/// requests, delegated to BIO-ACC-002's own already-reviewed idempotency guarantee via
/// <see cref="IAccessDecisionGateway"/> - see that interface's own header for why this feature
/// does not duplicate a second idempotency store on top of it).
/// </summary>
public sealed class VerificationService(
    IAccessDecisionGateway accessDecisionGateway,
    IVerificationDeviceRoleRepository deviceRoleRepository,
    IVerificationOverrideRepository overrideRepository,
    IVerificationAuditSink auditSink)
{
    public async Task<VerificationDecisionResponse> RequestDecisionAsync(
        ResolvedDevice device, RequestVerificationDecisionRequest request, string idempotencyKey,
        AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        var role = await deviceRoleRepository.GetAsync(device.TenantId, device.DeviceId, cancellationToken);
        if (!role.Permits(VerificationAction.ExecuteDecision))
        {
            throw VerificationError.RoleForbidden(VerificationAction.ExecuteDecision.ToWireString(), role.ToWireString());
        }

        Validate(request);

        var query = new VerificationDecisionQuery
        {
            PersonId = request.PersonId!,
            ProfileKey = string.IsNullOrWhiteSpace(request.ProfileKey) ? null : request.ProfileKey.Trim(),
            CredentialClass = request.CredentialClass!.Trim(),
            LocationId = request.LocationId!.Trim(),
            OccurredAt = request.OccurredAt,
            PresentedCardStatus = request.PresentedCardStatus!,
            PolicyKey = request.PolicyKey!,
            FingerprintSample = DecodeSample(request.FingerprintSample),
            FaceSample = DecodeSample(request.FaceSample),
            IdempotencyKey = idempotencyKey,
        };

        VerificationDecisionResult result;
        try
        {
            result = await accessDecisionGateway.DecideAsync(device.TenantId, query, actor, correlationId, cancellationToken);
        }
        catch (VerificationError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw VerificationError.DownstreamDecisionServiceFailed(ex.Message);
        }

        await AuditAsync(
            device.TenantId, actor, "mobile_verification.decision.requested",
            new AuditTarget("access_decision_event", result.EventId),
            result.Decision == "allow" ? AuditOutcome.Allowed : AuditOutcome.Denied,
            result.ReasonCode, correlationId, cancellationToken);

        return VerificationDecisionResponse.From(result);
    }

    public async Task<VerificationDecisionListResponse> ListHistoryAsync(
        ResolvedDevice device, string personId, CancellationToken cancellationToken)
    {
        var role = await deviceRoleRepository.GetAsync(device.TenantId, device.DeviceId, cancellationToken);
        if (!role.Permits(VerificationAction.ReadHistory))
        {
            throw VerificationError.RoleForbidden(VerificationAction.ReadHistory.ToWireString(), role.ToWireString());
        }

        if (!CanonicalId.IsPersonId(personId))
        {
            throw VerificationError.Validation([new ValidationErrorItem("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier")]);
        }

        var results = await accessDecisionGateway.ListForPersonAsync(device.TenantId, personId, cancellationToken);
        return new VerificationDecisionListResponse { Data = [.. results.Select(VerificationDecisionResponse.From)] };
    }

    /// <summary>AC03: Supervisor-only, mandatory reason_code (mirrors BIO-MOB-005's own DecisionReasonRequired). Records this feature's OWN fact ("this device overrode this decision") without mutating BIO-ACC-002's own AccessDecisionEvent - see VerificationOverride.cs's own header.</summary>
    public async Task<VerificationOverrideResponse> OverrideDecisionAsync(
        ResolvedDevice device, string decisionEventId, OverrideDecisionRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        var role = await deviceRoleRepository.GetAsync(device.TenantId, device.DeviceId, cancellationToken);
        if (!role.Permits(VerificationAction.OverrideDeny))
        {
            throw VerificationError.RoleForbidden(VerificationAction.OverrideDeny.ToWireString(), role.ToWireString());
        }

        if (!CanonicalId.IsEventId(decisionEventId))
        {
            throw VerificationError.Validation([new ValidationErrorItem("decision_event_id", "invalid_format", "must be a canonical evt_<uuidv7> id")]);
        }

        if (string.IsNullOrWhiteSpace(request.PersonId) || !CanonicalId.IsPersonId(request.PersonId))
        {
            throw VerificationError.Validation([new ValidationErrorItem("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier")]);
        }

        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            throw VerificationError.OverrideReasonRequired();
        }

        var entry = new VerificationOverride
        {
            Id = CanonicalId.NewEventId(),
            TenantId = device.TenantId,
            DecisionEventId = decisionEventId,
            PersonId = request.PersonId,
            DeviceId = device.DeviceId,
            ReasonCode = request.ReasonCode,
            OverriddenAt = UtcInstant.Now(),
        };

        await AuditAsync(
            device.TenantId, actor, "mobile_verification.decision.override",
            new AuditTarget("access_decision_event", decisionEventId), AuditOutcome.Allowed,
            request.ReasonCode, correlationId, cancellationToken);

        await overrideRepository.CreateAsync(entry, CancellationToken.None);

        return VerificationOverrideResponse.From(entry);
    }

    public async Task<DeviceRoleResponse> GetDeviceRoleAsync(ResolvedDevice device, CancellationToken cancellationToken)
    {
        var role = await deviceRoleRepository.GetAsync(device.TenantId, device.DeviceId, cancellationToken);
        return DeviceRoleResponse.From(device.DeviceId, role);
    }

    /// <summary>The one human-actor-RBAC-gated write in this feature (AC03) - tenant/actor/permission already checked by the endpoint before this is called, mirroring BIO-MOB-005's own <c>SetCapturePolicyAsync</c> pattern.</summary>
    public async Task<DeviceRoleResponse> SetDeviceRoleAsync(
        string tenantId, string deviceId, SetDeviceRoleRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        if (!CanonicalId.IsDeviceId(deviceId))
        {
            throw VerificationError.Validation([new ValidationErrorItem("device_id", "invalid_format", "must be a canonical dev_<uuidv7> id")]);
        }

        if (!VerificationDeviceRoleExtensions.TryParse(request.Role, out var role))
        {
            throw VerificationError.InvalidDeviceRole(request.Role ?? "");
        }

        await deviceRoleRepository.SetAsync(tenantId, deviceId, role, cancellationToken);

        await AuditAsync(
            tenantId, actor, "mobile_verification.device_role.assign",
            new AuditTarget("verification_device_role", deviceId), AuditOutcome.Allowed,
            "role_assigned", correlationId, cancellationToken);

        return DeviceRoleResponse.From(deviceId, role);
    }

    private static void Validate(RequestVerificationDecisionRequest request)
    {
        List<ValidationErrorItem>? errors = null;
        void Add(string field, string code, string message) => (errors ??= []).Add(new ValidationErrorItem(field, code, message));

        if (string.IsNullOrWhiteSpace(request.PersonId) || !CanonicalId.IsPersonId(request.PersonId))
        {
            Add("person_id", "invalid_format", "person_id must be a well-formed canonical per_ identifier");
        }

        if (string.IsNullOrWhiteSpace(request.CredentialClass)) Add("credential_class", "required", "credential_class is required");
        if (string.IsNullOrWhiteSpace(request.LocationId)) Add("location_id", "required", "location_id is required");
        if (string.IsNullOrWhiteSpace(request.PolicyKey)) Add("policy_key", "required", "policy_key is required");
        if (string.IsNullOrWhiteSpace(request.PresentedCardStatus)) Add("presented_card_status", "required", "presented_card_status is required");

        if (!string.IsNullOrWhiteSpace(request.OccurredAt) && !UtcInstant.TryParse(request.OccurredAt, out _))
        {
            Add("occurred_at", "invalid_format", "occurred_at must be an RFC 3339 UTC instant if present");
        }

        if (errors is not null)
        {
            throw VerificationError.Validation(errors);
        }
    }

    private static BiometricSampleInput? DecodeSample(BiometricSampleRequest? sample)
    {
        if (sample is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(sample.PayloadBase64) || string.IsNullOrWhiteSpace(sample.ContentType) || string.IsNullOrWhiteSpace(sample.CapturedAt))
        {
            throw VerificationError.Validation([new ValidationErrorItem("biometric_sample", "required", "a biometric sample requires payload_base64, content_type and captured_at")]);
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(sample.PayloadBase64);
        }
        catch (FormatException)
        {
            throw VerificationError.Validation([new ValidationErrorItem("biometric_sample.payload_base64", "invalid_format", "payload_base64 must be valid base64")]);
        }

        return new BiometricSampleInput { Payload = payload, ContentType = sample.ContentType, CapturedAtUtc = sample.CapturedAt };
    }

    private async Task AuditAsync(string tenantId, AuditActor actor, string action, AuditTarget target, AuditOutcome outcome, string reasonCode, string correlationId, CancellationToken cancellationToken)
    {
        var auditEvent = new VerificationAuditEvent(CanonicalId.NewEventId(), tenantId, actor, action, target, outcome, reasonCode, correlationId, DateTimeOffset.UtcNow);

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
            throw VerificationError.AuditUnavailable();
        }

        if (!exists)
        {
            throw VerificationError.AuditUnavailable();
        }
    }
}
