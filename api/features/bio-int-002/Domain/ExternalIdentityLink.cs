namespace Biolog.Features.Integrations.Domain;

/// <summary>
/// This story's OWN correlation record between one external system's own identifier for a
/// person (<see cref="ExternalKey"/>) and the BIO-IDN-001 PERSON it was imported into. Does NOT
/// touch BIO-IDN-001's own storage in any way - a purely additive, this-story-owned table, the
/// same pattern BIO-VIS-002's <c>IStrandedCardLedger</c> established for "a side table this
/// story owns to track its own relationship to another story's aggregate, without writing into
/// that aggregate's own repository."
/// </summary>
public sealed record ExternalIdentityLink
{
    public required string TenantId { get; init; }
    public required string MappingId { get; init; }
    public required string ExternalKey { get; init; }
    public required string PersonId { get; init; }
    public required string CreatedAt { get; init; }
}

/// <summary><c>internal</c> - same auditable seam every sibling story's own simple stores use.</summary>
internal interface IExternalIdentityLinkStore
{
    Task<ExternalIdentityLink?> FindAsync(string tenantId, string mappingId, string externalKey, CancellationToken cancellationToken = default);

    Task CreateAsync(ExternalIdentityLink link, CancellationToken cancellationToken = default);
}
