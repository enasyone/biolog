using Biolog.Features.InstitutionalIdentifiers.Api;
using Biolog.Features.InstitutionalIdentifiers.Audit;
using Biolog.Features.InstitutionalIdentifiers.Domain;
using Biolog.Features.InstitutionalIdentifiers.Identifiers;
using Biolog.Features.InstitutionalIdentifiers.Tenancy;

namespace Biolog.Features.InstitutionalIdentifiers;

/// <summary>
/// BIO-IDN-003-AC02: "Uniqueness rules are configurable per type." A separate, small service
/// from <see cref="InstitutionalIdentifierService"/> because it mutates a separate aggregate
/// (<see cref="IdentifierUniquenessRule"/>) - but <see cref="SetRuleAsync"/> shares its
/// <see cref="IdentifierUniquenessRuleLockRegistry"/> lock table with
/// <see cref="InstitutionalIdentifierService.AssignAsync"/> (BIO-IDN-003-QA-01, Seyi round 1):
/// a rule change for a <c>(Type, CustomLabel)</c> and an in-flight assignment relying on that
/// same rule DO need to serialize against one another, or a caller could observe (or produce)
/// a result based on a rule value that was already stale by the time it committed. See that
/// registry's header for the full rationale. Registered Scoped for the identical
/// per-request-tenant-provider reason as every sibling service in this codebase.
///
/// Still follows ADR-0003 Decision 7's audit-before-commit discipline even though
/// <see cref="IIdentifierUniquenessRuleRepository.UpsertAsync"/> is not itself a
/// compare-and-set - see <see cref="IdentifierUniquenessRule"/>'s header for why that omission
/// is safe here.
///
/// <b>BIO-IDN-003-QA-01 (Seyi round 1, closure item 2).</b> <see cref="SetRuleAsync"/> rejects
/// turning enforcement ON for a scope that already contains a duplicated value (which could
/// only have been created while enforcement was OFF, since <see cref="InstitutionalIdentifierService.AssignAsync"/>
/// itself would otherwise have refused it) - recording an <c>Enforced</c> rule the existing
/// data already violates would make the rule a lie the moment it was written. Reads
/// <see cref="IInstitutionalIdentifierRepository.HasDuplicateValuesAsync"/> under the SAME
/// outer lock this method already holds, so no assignment can slip a fresh duplicate in
/// between the check and the write.
/// </summary>
internal sealed class IdentifierUniquenessRuleService(
    ICurrentTenantProvider tenantProvider,
    IIdentifierUniquenessRuleRepository repository,
    IInstitutionalIdentifierRepository identifierRepository,
    IInstitutionalIdentifierAuditSink auditSink,
    IdentifierUniquenessRuleLockRegistry lockRegistry)
{
    public async Task<IdentifierUniquenessRule?> GetRuleAsync(IdentifierType type, string? customLabel, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.GetAsync(tenantId, type, customLabel, cancellationToken);
    }

    public async Task<IdentifierUniquenessRule> SetRuleAsync(
        IdentifierType type, string? customLabel, bool enforced, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(tenantId, type, customLabel, cancellationToken);
        var existing = await repository.GetAsync(tenantId, type, customLabel, cancellationToken);

        if (enforced && await identifierRepository.HasDuplicateValuesAsync(tenantId, type, customLabel, cancellationToken))
        {
            // Not itself audit-worthy: a request that never had a legitimate target (the
            // scope is already in violation) is rejected before anything is written, the same
            // posture every other pre-lock/pre-audit validation check in this feature takes.
            throw InstitutionalIdentifierError.UniquenessAlreadyViolated(type, customLabel);
        }

        var now = UtcInstant.Now();
        var rule = new IdentifierUniquenessRule(tenantId, type, customLabel)
        {
            Enforced = enforced,
            Version = (existing?.Version ?? 0) + 1,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        var target = new AuditTarget("identifier_uniqueness_rule", customLabel is null ? type.ToWireString() : $"{type.ToWireString()}/{customLabel}");
        var detail = new Dictionary<string, (object?, string)>
        {
            ["type"] = (type.ToWireString(), "internal"),
            ["custom_label"] = (customLabel, "internal"),
            ["enforced"] = (enforced, "internal"),
        };
        var auditEvent = InstitutionalIdentifierAuditEventBuilder.Build(
            tenantId, actor, "identifier_uniqueness_rule.set", target, AuditOutcome.Allowed, "uniqueness_rule_set", correlationId,
            causationId: null, detail, withheld: []);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch (Exception)
        {
            bool exists;
            try
            {
                exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
            }
            catch (Exception)
            {
                throw InstitutionalIdentifierError.AuditUnavailable();
            }

            if (!exists)
            {
                throw InstitutionalIdentifierError.AuditUnavailable();
            }
        }

        return await repository.UpsertAsync(rule, CancellationToken.None);
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw InstitutionalIdentifierError.TenantContextMissing();
}
