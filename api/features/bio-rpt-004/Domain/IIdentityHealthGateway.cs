namespace Biolog.Features.OperationalDashboard.Domain;

/// <summary>This feature's own whitelisted projection onto BIO-IDN-001's PERSON counts (BIO-RPT-004-AC01) - never the full <c>Person</c> record itself.</summary>
public sealed record IdentityCounts(int Total, int Active, int Suspended, int Other);

/// <summary>Real cross-story dependency (BIO-IDN-001). Implemented by an adapter in <c>BioRpt004Module</c> wrapping BIO-IDN-001's own already-registered <c>IPersonRepository.ListByTenantAsync</c> directly - that interface is <c>internal</c>, not public, but already grants <c>Biolog.Api</c> access via <c>InternalsVisibleTo</c> (BIO-IDN-001-QA-01), the same access every one of that story's own module registrations already uses.</summary>
public interface IIdentityHealthGateway
{
    Task<IdentityCounts> GetAsync(string tenantId, CancellationToken cancellationToken = default);
}
