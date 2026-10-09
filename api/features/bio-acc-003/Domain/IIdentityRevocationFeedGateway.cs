namespace Biolog.Features.AccessCache.Domain;

/// <summary>
/// BIO-ACC-003-AC01's "minimum necessary" applied at this gateway's own boundary, not only on
/// the wire: only what an edge device needs to deny a revoked person - never a name, a profile
/// field, or anything else PersonRegistry's own <c>Person</c> aggregate carries.
/// <paramref name="RevokedAt"/> mirrors the source <c>Person.UpdatedAt</c> exactly - the same
/// value this gateway's own cursor paging compares ordinally against.
/// </summary>
public sealed record RevocationEntry(string PersonId, string RevokedAt);

/// <summary>
/// This story's own scope decision (god, 2026-09-24, option (c) of the BIO-CRD-006 dependency
/// question): identity-level revocation only, via BIO-ACC-001's established
/// <c>IIdentityStatusGateway -> PersonRegistry.PersonService</c> pattern, extended here to a
/// delta feed rather than a point lookup. Card-level revocation (BIO-CRD-006's own deny-list -
/// see that story's <c>GET /api/v1/card-lifecycle/deny-list</c>) is explicitly OUT of this
/// story's scope: BIO-CRD-006 is not a declared dependency of BIO-ACC-003, and its endpoint has
/// no device/actor authentication of its own yet (flagged to god as its own follow-up, not
/// fixed here). A future story that adds BIO-CRD-006 as a declared dependency, with that
/// endpoint's own device-auth gap closed first, is the intended path to closing that gap - not
/// a quiet reach-in from this one.
///
/// The real implementation (an adapter in <c>Biolog.Api.Modules.BioAcc003Module</c>) wraps
/// PersonRegistry's own <c>ICrossTenantPersonRevocationLookup</c> (approved 2026-09-24,
/// mirroring <c>ICrossTenantDeviceLookup</c>'s identical shape/rationale) - device-authenticated
/// callers have no ambient tenant channel PersonService's own public surface requires.
/// </summary>
public interface IIdentityRevocationFeedGateway
{
    Task<IReadOnlyList<RevocationEntry>> ListRevokedSinceAsync(string tenantId, string? since, CancellationToken cancellationToken = default);
}
