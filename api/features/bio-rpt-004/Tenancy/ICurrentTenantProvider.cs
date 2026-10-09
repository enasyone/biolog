namespace Biolog.Features.OperationalDashboard.Tenancy;

/// <summary>
/// The same shape every sibling feature project in this epic already establishes (see
/// BIO-RPT-002's identical interface) - implemented by an adapter over
/// <c>Biolog.Api.Tenancy.ITenantContextAccessor</c> in <c>BioRpt004Module</c>, never referenced
/// directly. <see cref="TenantId"/> is <c>null</c> when the host channel resolved no tenant for
/// this request (ADR-0002 Decision 2: "there is no default tenant") - <see cref="DashboardService"/>
/// fails closed on that, never guesses one.
/// </summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
