using Biolog.Features.AccessPolicy.Api;
using Biolog.Features.AccessPolicy.Audit;
using Biolog.Features.AccessPolicy.Domain;
using Biolog.Features.AccessPolicy.Identifiers;
using Biolog.Features.AccessPolicy.Tenancy;

namespace Biolog.Features.AccessPolicy;

/// <summary>
/// Application service for BIO-ACC-001-AC03/AC04: evaluating a named policy's currently
/// PUBLISHED version against one WHO+CREDENTIAL+WHERE+WHEN+STATUS+ASSURANCE context
/// (<see cref="PolicyEvaluationContext"/>). The actual precedence/default-deny logic lives in the
/// pure, independently-testable <see cref="PolicyEvaluator"/> - this service's own job is
/// resolving the inputs (the published version, the subject's live identity status) and making
/// the decision auditable (AC04).
///
/// <b>Evaluation is itself an authorization decision (ADR-0006 Decision 1/2), not merely audit
/// attribution.</b> Unlike <see cref="AccessPolicyService"/>'s read methods, this endpoint is
/// gated behind <see cref="EvaluatePermission"/> AND <see cref="Api.ActorProvenanceGate"/> - an
/// unauthenticated/unprovenanced caller could otherwise use this endpoint as an oracle to probe a
/// tenant's policy rules (which credential classes, locations and schedules a given person is
/// allowed) one evaluation at a time.
///
/// <b>AC04 - every evaluation is audited, never silently decided.</b> Applies the same
/// audit-before-return discipline (ADR-0003 Decision 7) <see cref="AccessPolicyService"/> uses for
/// its own mutations: an audit-sink failure surfaces as <c>audit_unavailable</c> rather than
/// returning an unaudited decision - a deliberate, disclosed consequence that makes access
/// evaluation fail CLOSED on an audit-sink outage, matching AC03's own fail-closed philosophy.
/// </summary>
internal sealed class PolicyEvaluationService(
    ICurrentTenantProvider tenantProvider,
    IAccessPolicyRepository repository,
    IIdentityStatusGateway identityStatusGateway,
    IPermissionGateway permissionGateway,
    IAccessPolicyAuditSink auditSink)
{
    /// <summary>Required to invoke evaluation at all - see class header.</summary>
    public const string EvaluatePermission = "access_policy.evaluate";

    public async Task<PolicyEvaluationResult> EvaluateAsync(
        string policyKey, EvaluateAccessPolicyRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, EvaluatePermission, cancellationToken))
        {
            throw AccessPolicyError.PermissionRequired(EvaluatePermission);
        }

        var context = ParseContext(request);

        var policy = await repository.GetAsync(tenantId, policyKey, cancellationToken);
        var publishedVersion = policy?.GetPublishedVersion();
        var identityStatus = await identityStatusGateway.GetStatusAsync(context.PersonId, cancellationToken);

        var result = PolicyEvaluator.Evaluate(policyKey, publishedVersion, context, identityStatus);

        await AuditAsync(tenantId, actor, policyKey, context, result, correlationId, cancellationToken);
        return result;
    }

    private static PolicyEvaluationContext ParseContext(EvaluateAccessPolicyRequest request)
    {
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

        DateTime occurredAtUtc = default;
        if (request.OccurredAt is null)
        {
            occurredAtUtc = DateTime.UtcNow;
        }
        else if (!DateTime.TryParse(
            request.OccurredAt, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out occurredAtUtc))
        {
            errors.Add(new ValidationErrorItem("occurred_at", "invalid_format", "occurred_at must be RFC 3339 if present"));
        }

        if (!CardStatusExtensions.TryParse(request.PresentedCardStatus, out var presentedCardStatus))
        {
            errors.Add(new ValidationErrorItem("presented_card_status", "invalid_format", "presented_card_status must be issued, active or deactivated"));
        }

        if (!AssuranceLevelExtensions.TryParse(request.PresentedAssuranceLevel, out var presentedAssuranceLevel))
        {
            errors.Add(new ValidationErrorItem("presented_assurance_level", "invalid_format", "presented_assurance_level must be BASIC, ELEVATED or HIGH"));
        }

        if (errors.Count > 0)
        {
            throw AccessPolicyError.Validation(errors);
        }

        return new PolicyEvaluationContext
        {
            PersonId = request.PersonId!,
            ProfileKey = string.IsNullOrWhiteSpace(request.ProfileKey) ? null : request.ProfileKey.Trim(),
            CredentialClass = request.CredentialClass!.Trim(),
            LocationId = request.LocationId!.Trim(),
            OccurredAtUtc = occurredAtUtc,
            PresentedCardStatus = presentedCardStatus,
            PresentedAssuranceLevel = presentedAssuranceLevel,
        };
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string policyKey, PolicyEvaluationContext context, PolicyEvaluationResult result, string correlationId, CancellationToken cancellationToken)
    {
        var target = new AuditTarget("access_policy_evaluation", policyKey);
        var outcome = result.Decision == PolicyEvaluationDecision.Allow ? AuditOutcome.Allowed : AuditOutcome.Denied;
        var detail = new Dictionary<string, (object?, string)>
        {
            ["policy_key"] = (policyKey, "internal"),
            ["policy_version_number"] = (result.PolicyVersionNumber, "internal"),
            ["matched_rule_id"] = (result.MatchedRuleId, "internal"),
            ["credential_class"] = (context.CredentialClass, "internal"),
            ["location_id"] = (context.LocationId, "internal"),
            ["subject_person_id"] = (context.PersonId, "internal"),
        };
        var auditEvent = AccessPolicyAuditEventBuilder.Build(
            tenantId, actor, "access_policy.evaluate", target, outcome, result.ReasonCode, correlationId, causationId: null, detail, withheld: []);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return;
        }
        catch (Exception)
        {
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw AccessPolicyError.AuditUnavailable();
        }

        if (!exists)
        {
            throw AccessPolicyError.AuditUnavailable();
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw AccessPolicyError.TenantContextMissing();
}
