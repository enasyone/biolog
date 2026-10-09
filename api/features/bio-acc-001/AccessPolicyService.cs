using System.Text.RegularExpressions;
using Biolog.Features.AccessPolicy.Api;
using Biolog.Features.AccessPolicy.Audit;
using Biolog.Features.AccessPolicy.Domain;
using Biolog.Features.AccessPolicy.Identifiers;
using Biolog.Features.AccessPolicy.Tenancy;

namespace Biolog.Features.AccessPolicy;

/// <summary>
/// Application service for BIO-ACC-001-AC01/AC02: authoring, revising and publishing
/// <see cref="Domain.AccessPolicy"/> drafts. Implements ADR-0003 Decision 7's audit-atomicity
/// pattern exactly as established across this codebase's identity wave: a per-
/// <c>(TenantId, PolicyKey)</c> lock (<see cref="AccessPolicyLockRegistry"/>), audit-before-
/// commit, <see cref="IAccessPolicyAuditSink.ExistsAsync"/> verification on an ambiguous
/// audit-write throw, <see cref="CancellationToken.None"/> on the commit once the audit write is
/// confirmed.
///
/// Every method - mutating AND read - requires the calling actor to already hold the relevant
/// permission (<see cref="ManagePermission"/>/<see cref="PublishPermission"/>/
/// <see cref="ReadPermission"/>, via <see cref="IPermissionGateway"/> - this story's RBAC
/// integration seam, wrapping BIO-IDN-006's own <c>PermissionService.ActorHasPermissionAsync</c>).
///
/// <b>BIO-ACC-001-QA-01 (Chioma's acceptance audit, High).</b> Round 1 left <see cref="GetAsync"/>
/// and <see cref="ListAsync"/> unauthenticated, reasoning from BIO-IDN-006's own
/// <c>RoleAssignmentEndpoints.ListAsync</c> convention (read endpoints are open to any asserted
/// actor). That precedent does not transfer here: a role ASSIGNMENT list names one person's roles,
/// while an access POLICY's full content - named subjects, credential classes, locations,
/// schedules, DENY rules and required assurance - is itself the exact information a tenant most
/// needs to keep confidential (it is the blueprint for bypassing the tenant's own physical
/// security). An unauthenticated read endpoint let any caller who could reach the tenant context
/// enumerate that blueprint. Fixed by requiring <see cref="ReadPermission"/> (and, at the
/// endpoint, a verified provenance proof - ADR-0006 Decision 3) on both read methods, the same as
/// every mutating one - "read" and "write" are both authorization decisions for a policy whose own
/// CONTENT is the sensitive asset, not just its ability to change.
///
/// <b>AC02 - publish is a maker-checker step.</b> <see cref="PublishAsync"/> requires the
/// publisher to hold <see cref="PublishPermission"/> separately from
/// <see cref="ManagePermission"/> (a tenant can delegate "who may draft" and "who may publish" to
/// different roles), and <see cref="Domain.AccessPolicy.Publish"/> itself additionally rejects a
/// publisher who is the same actor as the draft's own author - two independent checks, not one.
/// </summary>
internal sealed partial class AccessPolicyService(
    ICurrentTenantProvider tenantProvider,
    IAccessPolicyRepository repository,
    IAccessPolicyAuditSink auditSink,
    AccessPolicyLockRegistry lockRegistry,
    IPersonExistenceGateway personGateway,
    IPermissionGateway permissionGateway)
{
    /// <summary>Required to create or revise a draft.</summary>
    public const string ManagePermission = "access_policy.manage";

    /// <summary>Required to publish a draft - checked in addition to, not instead of, <see cref="Domain.AccessPolicy.Publish"/>'s own author-vs-publisher maker-checker check.</summary>
    public const string PublishPermission = "access_policy.publish";

    /// <summary>BIO-ACC-001-QA-01: required to read a policy's content (GET/LIST) - a policy's full rule set is itself sensitive, not merely its ability to change.</summary>
    public const string ReadPermission = "access_policy.read";

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex PolicyKeyPattern();

    public async Task<Domain.AccessPolicy> CreateDraftAsync(
        CreateAccessPolicyRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequirePermissionAsync(tenantId, actor, ManagePermission, cancellationToken);

        var errors = new List<ValidationErrorItem>();
        var policyKey = ValidatePolicyKey(request.PolicyKey, errors);
        var rules = await ParseRulesAsync(request.Rules, errors, cancellationToken);
        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(tenantId, policyKey!, cancellationToken);
        if (await repository.GetAsync(tenantId, policyKey!, cancellationToken) is not null)
        {
            throw AccessPolicyError.PolicyAlreadyExists(policyKey!);
        }

        var now = UtcInstant.Now();
        var policy = Domain.AccessPolicy.Draft(tenantId, policyKey!, rules!, actor.Id, now);

        await AuditAsync(tenantId, actor, "access_policy.draft_created", policyKey!, "policy_created", correlationId, cancellationToken);
        return await repository.CreateAsync(policy, CancellationToken.None);
    }

    public async Task<Domain.AccessPolicy> ReviseDraftAsync(
        string policyKey, ReviseDraftRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequirePermissionAsync(tenantId, actor, ManagePermission, cancellationToken);

        var errors = new List<ValidationErrorItem>();
        var rules = await ParseRulesAsync(request.Rules, errors, cancellationToken);
        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
            expectedVersion = default;
        }

        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(tenantId, policyKey, cancellationToken);
        var current = await repository.GetAsync(tenantId, policyKey, cancellationToken) ?? throw AccessPolicyError.PolicyNotFound(policyKey);
        var updated = current.ReviseDraft(rules!, actor.Id, expectedVersion, UtcInstant.Now());

        await AuditAsync(tenantId, actor, "access_policy.draft_revised", policyKey, "draft_revised", correlationId, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<Domain.AccessPolicy> PublishAsync(
        string policyKey, PublishAccessPolicyRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequirePermissionAsync(tenantId, actor, PublishPermission, cancellationToken);

        var errors = new List<ValidationErrorItem>();
        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
            expectedVersion = default;
        }

        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(tenantId, policyKey, cancellationToken);
        var current = await repository.GetAsync(tenantId, policyKey, cancellationToken) ?? throw AccessPolicyError.PolicyNotFound(policyKey);
        var updated = current.Publish(actor.Id, expectedVersion, UtcInstant.Now());

        await AuditAsync(tenantId, actor, "access_policy.published", policyKey, "policy_published", correlationId, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<Domain.AccessPolicy> GetAsync(string policyKey, AuditActor actor, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequirePermissionAsync(tenantId, actor, ReadPermission, cancellationToken);
        return await repository.GetAsync(tenantId, policyKey, cancellationToken) ?? throw AccessPolicyError.PolicyNotFound(policyKey);
    }

    public async Task<IReadOnlyList<Domain.AccessPolicy>> ListAsync(AuditActor actor, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequirePermissionAsync(tenantId, actor, ReadPermission, cancellationToken);
        return await repository.ListAsync(tenantId, cancellationToken);
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string policyKey, string reasonCode, string correlationId, CancellationToken cancellationToken)
    {
        var target = new AuditTarget("access_policy", policyKey);
        var detail = new Dictionary<string, (object?, string)> { ["policy_key"] = (policyKey, "internal") };
        var auditEvent = AccessPolicyAuditEventBuilder.Build(
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
            throw AccessPolicyError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw AccessPolicyError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as success.
    }

    private async Task RequirePermissionAsync(string tenantId, AuditActor actor, string permissionKey, CancellationToken cancellationToken)
    {
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, permissionKey, cancellationToken))
        {
            throw AccessPolicyError.PermissionRequired(permissionKey);
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw AccessPolicyError.TenantContextMissing();

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw AccessPolicyError.Validation(errors);
        }
    }

    private static string? ValidatePolicyKey(string? value, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new ValidationErrorItem("policy_key", "required", "policy_key is required"));
            return null;
        }

        var trimmed = value.Trim();
        if (!PolicyKeyPattern().IsMatch(trimmed))
        {
            errors.Add(new ValidationErrorItem("policy_key", "invalid_format", "policy_key must be lowercase alphanumeric/hyphen, at most 64 characters, starting with a letter or digit"));
            return null;
        }

        return trimmed;
    }

    // --- Rule parsing (wire DTOs -> validated domain PolicyRule list) ---

    private async Task<List<PolicyRule>?> ParseRulesAsync(IReadOnlyList<PolicyRuleRequest>? rules, List<ValidationErrorItem> errors, CancellationToken cancellationToken)
    {
        if (rules is null || rules.Count == 0)
        {
            errors.Add(new ValidationErrorItem("rules", "required", "at least one rule is required"));
            return null;
        }

        var parsed = new List<PolicyRule>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = await ParseRuleAsync(rules[i], i, errors, cancellationToken);
            if (rule is not null)
            {
                parsed.Add(rule);
            }
        }

        return errors.Count == 0 ? parsed : null;
    }

    /// <summary>Position in the array determines <see cref="PolicyRule.RuleId"/> (1-based) - any <c>rule_id</c> the caller sends is ignored, so there is no separate "does rule_id match its position" validation dimension.</summary>
    private async Task<PolicyRule?> ParseRuleAsync(PolicyRuleRequest request, int index, List<ValidationErrorItem> errors, CancellationToken cancellationToken)
    {
        var path = $"rules[{index}]";
        var startingErrorCount = errors.Count;

        if (!PolicyRuleEffectExtensions.TryParse(request.Effect, out var effect))
        {
            errors.Add(new ValidationErrorItem($"{path}.effect", "invalid_format", "effect must be ALLOW or DENY"));
        }

        var subject = await ParseSubjectAsync(request.Subject, path, errors, cancellationToken);
        var credential = ParseCredential(request.Credential, path, errors);
        var location = ParseLocation(request.Location, path, errors);
        var schedule = ParseSchedule(request.Schedule, path, errors);
        var identityStatus = ParseIdentityStatusMatch(request.IdentityStatus, path, errors);
        var cardStatus = ParseCardStatusMatch(request.CardStatus, path, errors);

        if (!AssuranceLevelExtensions.TryParse(request.RequiredAssuranceLevel, out var requiredAssurance))
        {
            errors.Add(new ValidationErrorItem($"{path}.required_assurance_level", "invalid_format", "required_assurance_level must be BASIC, ELEVATED or HIGH"));
        }

        if (errors.Count != startingErrorCount)
        {
            return null;
        }

        return new PolicyRule
        {
            RuleId = index + 1,
            Effect = effect,
            Subject = subject!,
            Credential = credential!,
            Location = location!,
            Schedule = schedule!,
            IdentityStatus = identityStatus!,
            CardStatus = cardStatus!,
            RequiredAssuranceLevel = requiredAssurance,
        };
    }

    private async Task<SubjectMatch?> ParseSubjectAsync(SubjectMatchRequest? request, string path, List<ValidationErrorItem> errors, CancellationToken cancellationToken)
    {
        if (request?.Kind is null || !SubjectMatchKindExtensions.TryParse(request.Kind, out var kind))
        {
            errors.Add(new ValidationErrorItem($"{path}.subject.kind", "invalid_format", "kind must be ANY_PERSON, SPECIFIC_PERSON or PROFILE_KEY"));
            return null;
        }

        switch (kind)
        {
            case SubjectMatchKind.AnyPerson:
                return SubjectMatch.Any;

            case SubjectMatchKind.SpecificPerson:
                if (string.IsNullOrWhiteSpace(request.Value) || !CanonicalId.IsPersonId(request.Value))
                {
                    errors.Add(new ValidationErrorItem($"{path}.subject.value", "invalid_format", "value must be a well-formed canonical per_ identifier for SPECIFIC_PERSON"));
                    return null;
                }

                if (!await personGateway.ExistsAsync(request.Value, cancellationToken))
                {
                    throw AccessPolicyError.SubjectPersonNotFound(request.Value);
                }

                return new SubjectMatch(kind, request.Value);

            case SubjectMatchKind.ProfileKey:
                if (string.IsNullOrWhiteSpace(request.Value))
                {
                    errors.Add(new ValidationErrorItem($"{path}.subject.value", "required", "value is required for PROFILE_KEY"));
                    return null;
                }

                return new SubjectMatch(kind, request.Value.Trim());

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch");
        }
    }

    private static CredentialClassMatch? ParseCredential(CredentialClassMatchRequest? request, string path, List<ValidationErrorItem> errors)
    {
        if (request?.Kind is null || !CredentialClassMatchKindExtensions.TryParse(request.Kind, out var kind))
        {
            errors.Add(new ValidationErrorItem($"{path}.credential.kind", "invalid_format", "kind must be ANY or SPECIFIC_CLASS"));
            return null;
        }

        if (kind == CredentialClassMatchKind.Any)
        {
            return CredentialClassMatch.Any;
        }

        if (string.IsNullOrWhiteSpace(request.CredentialClass))
        {
            errors.Add(new ValidationErrorItem($"{path}.credential.credential_class", "required", "credential_class is required for SPECIFIC_CLASS"));
            return null;
        }

        return new CredentialClassMatch(kind, request.CredentialClass.Trim());
    }

    private static LocationMatch? ParseLocation(LocationMatchRequest? request, string path, List<ValidationErrorItem> errors)
    {
        if (request?.Kind is null || !LocationMatchKindExtensions.TryParse(request.Kind, out var kind))
        {
            errors.Add(new ValidationErrorItem($"{path}.location.kind", "invalid_format", "kind must be ANY or SPECIFIC_LOCATION"));
            return null;
        }

        if (kind == LocationMatchKind.Any)
        {
            return LocationMatch.Any;
        }

        if (string.IsNullOrWhiteSpace(request.LocationId))
        {
            errors.Add(new ValidationErrorItem($"{path}.location.location_id", "required", "location_id is required for SPECIFIC_LOCATION"));
            return null;
        }

        return new LocationMatch(kind, request.LocationId.Trim());
    }

    private static ScheduleMatch? ParseSchedule(ScheduleMatchRequest? request, string path, List<ValidationErrorItem> errors)
    {
        if (request?.Kind is null || !ScheduleMatchKindExtensions.TryParse(request.Kind, out var kind))
        {
            errors.Add(new ValidationErrorItem($"{path}.schedule.kind", "invalid_format", "kind must be ALWAYS or WINDOWS"));
            return null;
        }

        if (kind == ScheduleMatchKind.Always)
        {
            return ScheduleMatch.Always;
        }

        var windowRequests = request.Windows ?? [];
        if (windowRequests.Count == 0)
        {
            errors.Add(new ValidationErrorItem($"{path}.schedule.windows", "required", "at least one window is required for WINDOWS"));
            return null;
        }

        var startingErrorCount = errors.Count;
        var windows = new List<ScheduleWindow>();
        for (var i = 0; i < windowRequests.Count; i++)
        {
            var w = windowRequests[i];
            var windowPath = $"{path}.schedule.windows[{i}]";
            if (!Enum.TryParse<DayOfWeek>(w.DayOfWeek, ignoreCase: true, out var day) || !Enum.IsDefined(day))
            {
                errors.Add(new ValidationErrorItem($"{windowPath}.day_of_week", "invalid_format", "day_of_week must be a full English day name, e.g. MONDAY"));
                continue;
            }

            if (!TimeOnly.TryParse(w.StartTime, out var start) || !TimeOnly.TryParse(w.EndTime, out var end))
            {
                errors.Add(new ValidationErrorItem(windowPath, "invalid_format", "start_time and end_time must be HH:mm"));
                continue;
            }

            if (start >= end)
            {
                errors.Add(new ValidationErrorItem(windowPath, "invalid_range", "start_time must be strictly before end_time - an overnight-spanning window is not supported; use two windows instead"));
                continue;
            }

            windows.Add(new ScheduleWindow(day, start, end));
        }

        return errors.Count == startingErrorCount ? new ScheduleMatch(kind, windows) : null;
    }

    private static IdentityStatusMatch? ParseIdentityStatusMatch(IdentityStatusMatchRequest? request, string path, List<ValidationErrorItem> errors)
    {
        if (request?.AnyStatus == true)
        {
            return IdentityStatusMatch.Any;
        }

        var allowed = request?.AllowedStatuses ?? [];
        if (allowed.Count == 0)
        {
            errors.Add(new ValidationErrorItem($"{path}.identity_status", "required", "set any_status=true or provide at least one allowed_statuses value"));
            return null;
        }

        var startingErrorCount = errors.Count;
        var set = new HashSet<IdentityStatus>();
        foreach (var value in allowed)
        {
            if (!IdentityStatusExtensions.TryParse(value, out var status))
            {
                errors.Add(new ValidationErrorItem($"{path}.identity_status.allowed_statuses", "invalid_format", $"'{value}' is not a recognised identity status"));
                continue;
            }

            set.Add(status);
        }

        return errors.Count == startingErrorCount ? new IdentityStatusMatch(false, set) : null;
    }

    private static CardStatusMatch? ParseCardStatusMatch(CardStatusMatchRequest? request, string path, List<ValidationErrorItem> errors)
    {
        if (request?.AnyStatus == true)
        {
            return CardStatusMatch.Any;
        }

        var allowed = request?.AllowedStatuses ?? [];
        if (allowed.Count == 0)
        {
            errors.Add(new ValidationErrorItem($"{path}.card_status", "required", "set any_status=true or provide at least one allowed_statuses value"));
            return null;
        }

        var startingErrorCount = errors.Count;
        var set = new HashSet<CardStatus>();
        foreach (var value in allowed)
        {
            if (!CardStatusExtensions.TryParse(value, out var status))
            {
                errors.Add(new ValidationErrorItem($"{path}.card_status.allowed_statuses", "invalid_format", $"'{value}' is not a recognised card status"));
                continue;
            }

            set.Add(status);
        }

        return errors.Count == startingErrorCount ? new CardStatusMatch(false, set) : null;
    }
}
