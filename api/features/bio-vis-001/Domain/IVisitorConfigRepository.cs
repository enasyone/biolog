namespace Biolog.Features.Visitors.Domain;

public interface IVisitorConfigRepository
{
    Task<VisitorTenantConfig> GetAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set: fails (returns false) if <paramref name="expectedVersion"/> no longer matches what is stored.</summary>
    Task<bool> TrySaveAsync(VisitorTenantConfig config, int expectedVersion, CancellationToken cancellationToken = default);
}
