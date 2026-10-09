using Biolog.Features.BarcodeEngine.Model;

namespace Biolog.Features.ParserRegistry.Model;

/// <summary>
/// The parser store and workflow orchestrator (BIO-DOC-005-AC01/AC02/AC04/AC05): enforces who may
/// author a parser (AC01) and fails closed on cross-tenant visibility (AC05, the same pattern
/// BIO-DOC-001's <c>InMemoryDocumentSessionStore</c> established) before delegating the actual
/// version-workflow mutation to <see cref="ParserRecord"/>.
/// </summary>
public sealed class ParserCatalog
{
    private readonly Dictionary<string, ParserRecord> _records = [];
    private readonly TenantParserAuthorityRegistry _authority;

    public ParserCatalog(TenantParserAuthorityRegistry authority)
    {
        _authority = authority;
    }

    public ParserRecord Draft(
        ParserCaller caller,
        string parserId,
        ParserOwnerScope ownerScope,
        ParserApplicability applicability,
        IReadOnlyList<ParserFieldMapping> fieldMappings,
        IReadOnlyList<ParserValidationRule> validationRules,
        string authorActorId,
        string nowUtc)
    {
        RequireAuthorityToAuthor(caller, ownerScope);
        if (_records.ContainsKey(parserId))
        {
            throw new InvalidOperationException($"parser_id_already_exists: '{parserId}'.");
        }

        var record = ParserRecord.Draft(parserId, ownerScope, applicability, fieldMappings, validationRules, authorActorId, nowUtc);
        _records[parserId] = record;
        return record;
    }

    /// <summary>Fails closed (BIO-DOC-005-AC05): a parser that does not exist and one that exists
    /// but is not visible to <paramref name="caller"/> raise the identical
    /// <see cref="ParserNotFoundException"/>.</summary>
    public ParserRecord Get(ParserCaller caller, string parserId)
    {
        if (!_records.TryGetValue(parserId, out var record) || !record.OwnerScope.IsVisibleTo(caller))
        {
            throw new ParserNotFoundException(parserId);
        }

        return record;
    }

    public void ReviseDraft(
        ParserCaller caller,
        string parserId,
        ParserApplicability applicability,
        IReadOnlyList<ParserFieldMapping> fieldMappings,
        IReadOnlyList<ParserValidationRule> validationRules,
        string authorActorId,
        int expectedConcurrencyVersion,
        string nowUtc)
    {
        var record = Get(caller, parserId);
        RequireAuthorityToAuthor(caller, record.OwnerScope);
        record.ReviseDraft(applicability, fieldMappings, validationRules, authorActorId, expectedConcurrencyVersion, nowUtc);
    }

    public void RecordTestResult(ParserCaller caller, string parserId, bool passed, string testedByActorId, int expectedConcurrencyVersion, string nowUtc)
    {
        var record = Get(caller, parserId);
        RequireAuthorityToAuthor(caller, record.OwnerScope);
        record.RecordTestResult(passed, testedByActorId, expectedConcurrencyVersion, nowUtc);
    }

    public void Publish(ParserCaller caller, string parserId, string approverActorId, int expectedConcurrencyVersion, string nowUtc)
    {
        var record = Get(caller, parserId);
        RequireAuthorityToAuthor(caller, record.OwnerScope);
        record.Publish(approverActorId, expectedConcurrencyVersion, nowUtc);
    }

    /// <summary>Finds the published version applicable to a decoded document, among parsers
    /// visible to <paramref name="caller"/> (BIO-DOC-005-AC05 applies here too - a tenant never
    /// resolves against another tenant's parser). A tenant-scoped match is preferred over a
    /// global one when both apply, so a tenant can override the platform default for its own
    /// document formats.</summary>
    public ParserVersionContent? ResolvePublished(ParserCaller caller, BarcodeSymbology symbology, string issuingAuthority, string? documentCode)
    {
        var matches = _records.Values
            .Where(r => r.OwnerScope.IsVisibleTo(caller))
            .Select(r => (Record: r, Version: r.GetPublishedVersion()))
            .Where(x => x.Version is not null && x.Version.Applicability.Matches(symbology, issuingAuthority, documentCode))
            .ToList();

        if (matches.Count == 0)
        {
            return null;
        }

        var tenantMatch = matches.FirstOrDefault(x => x.Record.OwnerScope is ParserOwnerScope.Tenant);
        return (tenantMatch.Record is not null ? tenantMatch : matches[0]).Version;
    }

    private void RequireAuthorityToAuthor(ParserCaller caller, ParserOwnerScope ownerScope)
    {
        if (ownerScope is ParserOwnerScope.Global)
        {
            if (caller is not ParserCaller.Bts)
            {
                throw new InvalidOperationException("only_bts_may_author_global_parsers");
            }

            return;
        }

        if (ownerScope is ParserOwnerScope.Tenant tenantScope)
        {
            if (caller is ParserCaller.Bts)
            {
                return;
            }

            if (caller is ParserCaller.Tenant callerTenant && callerTenant.TenantId == tenantScope.TenantId)
            {
                if (!_authority.IsAuthorized(tenantScope.TenantId))
                {
                    throw new InvalidOperationException($"tenant_not_authorized_to_author_parsers: '{tenantScope.TenantId}'.");
                }

                return;
            }

            throw new InvalidOperationException("caller_may_not_author_for_another_tenant");
        }
    }
}
