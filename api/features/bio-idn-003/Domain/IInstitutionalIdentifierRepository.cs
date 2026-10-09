namespace Biolog.Features.InstitutionalIdentifiers.Domain;

/// <summary>
/// Storage for <see cref="InstitutionalIdentifier"/>, keyed by
/// <c>(PersonId, Type, CustomLabel, SequenceNumber)</c>. Deliberately has NO delete/remove
/// method (AC03's "history/status is preserved" ethos, identical to BIO-IDN-002's
/// <c>IPersonProfileRepository</c>: retiring an identifier - the only lifecycle exit - never
/// removes the record).
///
/// <b><c>internal</c>, from the start</b> (BIO-IDN-001-QA-01's lesson). A public repository
/// plus a public aggregate constructor would let any code that can reference this assembly
/// bypass <see cref="InstitutionalIdentifierService"/> entirely - skip the tenant boundary, the
/// existence check, the uniqueness rule and the audit trail. This interface, its
/// implementation, and <see cref="InstitutionalIdentifier"/>'s constructor are all
/// <c>internal</c>; <c>InternalsVisibleTo</c> in this project's <c>.csproj</c> grants the same
/// access to <c>Biolog.Api</c> (DI registration) and
/// <c>Biolog.Features.InstitutionalIdentifiers.Tests</c>. <see cref="InMemoryInstitutionalIdentifierRepository"/>
/// also independently re-validates identity immutability and initial state at the persistence
/// boundary, not merely trusting restricted access - the same dual-boundary discipline
/// <c>CredentialReferenceValidator</c> (BIO-DEV-001-QA-04) established.
///
/// Every read and write method takes <c>tenantId</c> explicitly and asserts a matching
/// record's own <see cref="InstitutionalIdentifier.TenantId"/> before returning it - the same
/// belt-and-braces rule every sibling tenant-plane story's repository applies.
/// </summary>
internal interface IInstitutionalIdentifierRepository
{
    Task<InstitutionalIdentifier> CreateAsync(InstitutionalIdentifier identifier, CancellationToken cancellationToken = default);

    Task<InstitutionalIdentifier?> GetActiveAsync(
        string tenantId, string personId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default);

    Task<InstitutionalIdentifier?> GetAsync(
        string tenantId, string personId, IdentifierType type, string? customLabel, int sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>Every record ever created for this exact (person, type, custom_label), oldest first - AC03's evidence that history is preserved.</summary>
    Task<IReadOnlyList<InstitutionalIdentifier>> ListHistoryAsync(
        string tenantId, string personId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstitutionalIdentifier>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    /// <summary>AC02's tenant-wide scan when a type's uniqueness rule is Enforced - across every person in the tenant, active and retired records alike (a retired value is never silently recycled).</summary>
    Task<bool> ValueExistsAsync(
        string tenantId, IdentifierType type, string? customLabel, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// BIO-IDN-003-QA-01 (Seyi round 1): whether two or more records already share the same
    /// <see cref="InstitutionalIdentifier.Value"/> within this exact <c>(Type, CustomLabel)</c>
    /// scope, tenant-wide, across active and retired records alike - the same population
    /// <see cref="ValueExistsAsync"/> checks. <see cref="IdentifierUniquenessRuleService.SetRuleAsync"/>
    /// consults this before ENABLING enforcement, so a tenant can never end up with an
    /// <see cref="IdentifierUniquenessRule.Enforced"/> rule that the data already violates.
    /// </summary>
    Task<bool> HasDuplicateValuesAsync(
        string tenantId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expected"/> (ADR-0001 Decision 3.2).</summary>
    Task<InstitutionalIdentifier> ReplaceAsync(InstitutionalIdentifier expected, InstitutionalIdentifier updated, CancellationToken cancellationToken = default);
}
