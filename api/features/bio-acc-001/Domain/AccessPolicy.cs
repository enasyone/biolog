namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// One tenant's named access policy (BIO-ACC-001-AC01/AC02): a full, permanent version history
/// of <see cref="PolicyRule"/> sets, keyed by <c>(TenantId, PolicyKey)</c> - a plain business
/// identifier, not a new ADR-0001 canonical id type, mirroring BIO-IDN-006's own
/// <c>ApprovalRequest</c> "prefer a natural key" precedent.
///
/// <b>Immutable, not a mutable class.</b> BIO-DOC-005's own <c>ParserRecord</c> models this exact
/// Draft/Publish version-workflow shape as a MUTABLE class holding a mutable version list. This
/// aggregate instead stays a fully immutable record (the same shape as BIO-IDN-006's
/// <c>PersonRoleAssignment</c>): every mutation returns a brand new <see cref="AccessPolicy"/>
/// via <c>with</c>, and <see cref="Domain.InMemoryAccessPolicyRepository"/> commits it with a
/// compare-and-set (<c>ConcurrentDictionary.TryUpdate</c>) exactly like every other repository in
/// this codebase's identity wave. A mutable-in-place aggregate shared by reference through a
/// dictionary risks an unlocked concurrent reader observing a torn/partially-updated object (one
/// field bumped, another not yet) - immutability plus CAS avoids that class of bug entirely,
/// without needing every read path to also take the per-key lock.
///
/// <b>ADR-0003 Decision 7's audit-atomicity, adapted for an immutable aggregate.</b>
/// <see cref="ReviseDraft"/> and <see cref="Publish"/> validate everything and return the NEW
/// value without touching any shared state - the same "build the new value, then audit, then
/// commit" three-step discipline <c>RoleAssignmentService</c> uses, just expressed as a pure
/// function here instead of inline in the service.
/// </summary>
public sealed record AccessPolicy
{
    public string TenantId { get; }
    public string PolicyKey { get; }
    public required int ConcurrencyVersion { get; init; }
    public required int? PublishedVersionNumber { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required IReadOnlyList<AccessPolicyVersionContent> Versions { get; init; }

    public AccessPolicyVersionContent Latest => Versions[^1];

    public AccessPolicyVersionContent? GetPublishedVersion() =>
        PublishedVersionNumber is int number ? Versions.Single(v => v.VersionNumber == number) : null;

    internal AccessPolicy(string tenantId, string policyKey)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("tenantId must not be blank", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(policyKey))
        {
            throw new ArgumentException("policyKey must not be blank", nameof(policyKey));
        }

        TenantId = tenantId;
        PolicyKey = policyKey;
    }

    public static AccessPolicy Draft(
        string tenantId, string policyKey, IReadOnlyList<PolicyRule> rules, string authorActorId, string nowUtc)
    {
        RequireNonEmpty(authorActorId, nameof(authorActorId));
        RequireNonEmpty(nowUtc, nameof(nowUtc));
        ValidateRuleSet(rules);

        var version = new AccessPolicyVersionContent(1, rules, AccessPolicyStatus.Draft, authorActorId, publishedByActorId: null, nowUtc, publishedAt: null);
        return new AccessPolicy(tenantId, policyKey)
        {
            ConcurrencyVersion = 1, PublishedVersionNumber = null, CreatedAt = nowUtc, UpdatedAt = nowUtc, Versions = [version],
        };
    }

    /// <summary>Edits the policy's rules. In place while the latest version is still Draft; otherwise starts a new Draft version (AC02) - the latest Published version's rules are never altered.</summary>
    public AccessPolicy ReviseDraft(IReadOnlyList<PolicyRule> rules, string authorActorId, int expectedConcurrencyVersion, string nowUtc)
    {
        RequireNonEmpty(authorActorId, nameof(authorActorId));
        RequireNonEmpty(nowUtc, nameof(nowUtc));
        ValidateRuleSet(rules);
        CheckVersion(expectedConcurrencyVersion);

        var latest = Latest;
        List<AccessPolicyVersionContent> versions = [.. Versions];
        if (latest.Status == AccessPolicyStatus.Draft)
        {
            versions[^1] = new AccessPolicyVersionContent(
                latest.VersionNumber, rules, AccessPolicyStatus.Draft, authorActorId, publishedByActorId: null, latest.CreatedAt, publishedAt: null);
        }
        else
        {
            versions.Add(new AccessPolicyVersionContent(
                latest.VersionNumber + 1, rules, AccessPolicyStatus.Draft, authorActorId, publishedByActorId: null, nowUtc, publishedAt: null));
        }

        return this with { Versions = versions, ConcurrencyVersion = ConcurrencyVersion + 1, UpdatedAt = nowUtc };
    }

    /// <summary>Publishes the latest Draft version (AC02) - requires the publisher to be a different actor from the version's own author (maker-checker, no self-approval, mirroring BIO-DOC-005's <c>ParserRecord.Publish</c>).</summary>
    public AccessPolicy Publish(string publisherActorId, int expectedConcurrencyVersion, string nowUtc)
    {
        RequireNonEmpty(publisherActorId, nameof(publisherActorId));
        RequireNonEmpty(nowUtc, nameof(nowUtc));
        CheckVersion(expectedConcurrencyVersion);

        var latest = Latest;
        if (latest.Status != AccessPolicyStatus.Draft)
        {
            throw AccessPolicyError.PolicyVersionNotDraft(PolicyKey, latest.Status);
        }

        if (publisherActorId == latest.AuthorActorId)
        {
            throw AccessPolicyError.SelfApprovalForbidden();
        }

        List<AccessPolicyVersionContent> versions = [.. Versions];
        versions[^1] = new AccessPolicyVersionContent(
            latest.VersionNumber, latest.Rules, AccessPolicyStatus.Published, latest.AuthorActorId, publisherActorId, latest.CreatedAt, publishedAt: nowUtc);

        return this with { Versions = versions, ConcurrencyVersion = ConcurrencyVersion + 1, UpdatedAt = nowUtc, PublishedVersionNumber = latest.VersionNumber };
    }

    /// <summary>AC01/AC03's structural baseline: at least one rule, unique sequential 1-based <see cref="PolicyRule.RuleId"/>s, so evaluation and audit can always cite a specific rule unambiguously.</summary>
    private static void ValidateRuleSet(IReadOnlyList<PolicyRule> rules)
    {
        if (rules.Count == 0)
        {
            throw new ArgumentException("a policy version must contain at least one rule", nameof(rules));
        }

        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i].RuleId != i + 1)
            {
                throw new ArgumentException("rule_id must be sequential and 1-based within the version", nameof(rules));
            }
        }
    }

    private void CheckVersion(int expectedVersion)
    {
        if (expectedVersion != ConcurrencyVersion)
        {
            throw AccessPolicyError.ConcurrencyConflict(PolicyKey, expectedVersion, ConcurrencyVersion);
        }
    }

    private static void RequireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("must be a non-empty string", paramName);
        }
    }
}
