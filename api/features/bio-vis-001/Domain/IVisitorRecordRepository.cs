namespace Biolog.Features.Visitors.Domain;

/// <summary>
/// Storage for <see cref="VisitorRecord"/>. Every read/write takes <c>tenantId</c> explicitly
/// and asserts a matching record's own <see cref="VisitorRecord.TenantId"/> before returning it -
/// the same belt-and-braces rule <c>InMemoryPersonRepository</c> applies on top of the service's
/// own tenant-context check.
///
/// <c>internal</c> (same BIO-IDN-001-QA-01 discipline every identity-wave aggregate applies) -
/// restricted to this assembly plus <c>Biolog.Api</c> (DI registration) and
/// <c>Biolog.Features.Visitors.Tests</c> via <c>InternalsVisibleTo</c>.
/// </summary>
internal interface IVisitorRecordRepository
{
    Task<VisitorRecord> CreateAsync(VisitorRecord record, CancellationToken cancellationToken = default);

    Task<VisitorRecord?> GetAsync(string tenantId, string visitorId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VisitorRecord>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expectedCurrent"/> (ADR-0001 Decision 3.2).</summary>
    Task<VisitorRecord> ReplaceAsync(VisitorRecord expectedCurrent, VisitorRecord updated, CancellationToken cancellationToken = default);
}
