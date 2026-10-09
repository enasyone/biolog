namespace Biolog.Features.InstitutionalIdentifiers.Domain;

/// <summary>
/// Storage for <see cref="IdentifierUniquenessRule"/>, keyed by
/// <c>(TenantId, Type, CustomLabel)</c>. <c>internal</c>, matching
/// <see cref="IInstitutionalIdentifierRepository"/>'s reasoning - <c>InternalsVisibleTo</c>
/// grants <c>Biolog.Api</c> and this project's own test assembly the only access.
///
/// <see cref="UpsertAsync"/> deliberately performs an unconditional write, not a
/// compare-and-set - see <see cref="IdentifierUniquenessRule"/>'s header for why that is safe
/// and correct here (the caller always holds the per-key lock from
/// <see cref="IdentifierUniquenessRuleLockRegistry"/> across the whole read-decide-write
/// sequence, so there is never more than one writer touching a given key at a time).
/// </summary>
internal interface IIdentifierUniquenessRuleRepository
{
    Task<IdentifierUniquenessRule?> GetAsync(string tenantId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default);

    Task<IdentifierUniquenessRule> UpsertAsync(IdentifierUniquenessRule rule, CancellationToken cancellationToken = default);
}
