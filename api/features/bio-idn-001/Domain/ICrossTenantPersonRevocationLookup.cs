namespace Biolog.Features.PersonRegistry.Domain;

/// <summary>
/// The single, deliberate exception to every other read on this feature requiring an already-
/// resolved ambient tenant (<see cref="PersonService.RequireTenant"/>) - mirrors
/// <c>Biolog.Api.Features.DeviceRegistry.ICrossTenantDeviceLookup</c> exactly, added for the
/// same reason under the same ruling (Kemi, BIO-MOB-003 acceptance FAIL 2026-09-23, extended
/// to BIO-ACC-003 2026-09-24): a device-authenticated caller has no host/principal channel and
/// therefore no ambient <c>TenantContext</c> to read, so it must supply the tenant it already
/// proved possession of (via device-secret authentication) explicitly, as a parameter, rather
/// than through <see cref="PersonService"/>'s ambient-only surface.
///
/// <b>Scope: revocation-status reads only, not a general cross-tenant person query.</b> Returns
/// only <see cref="PersonStatus.Revoked"/> records - BIO-ACC-003's own scope decision (identity-
/// level revocation only; card-level revocation is explicitly out of scope pending a declared
/// BIO-CRD-006 dependency) - never a caller-selectable status filter, so this seam cannot be
/// repurposed into "read any person in any tenant."
///
/// <c>internal</c>, not public - reachable only from inside this assembly (in practice, a
/// manual factory lambda in <c>Biolog.Api.Modules.BioAcc003Module</c>, never through DI's
/// reflection-based activator, so this internal visibility is no obstacle - the identical
/// construction discipline <see cref="ICrossTenantDeviceLookup"/> established). Never
/// registered against or exposed through <see cref="IPersonRepository"/>'s own tenant-scoped
/// surface or <see cref="PersonService"/>.
/// </summary>
internal interface ICrossTenantPersonRevocationLookup
{
    /// <summary>
    /// Every <see cref="PersonStatus.Revoked"/> person in <paramref name="tenantId"/>, ordered
    /// by <see cref="Person.UpdatedAt"/> then <see cref="Person.Id"/> (a stable tiebreak for
    /// entries sharing an instant). <paramref name="since"/>, when supplied, is an inclusive
    /// lower bound on <see cref="Person.UpdatedAt"/> (ordinal string comparison - ADR-0001
    /// Decision 2.1's fixed-width UTC instant rendering sorts correctly this way) - the same
    /// "inclusive cursor, idempotent re-delivery of the boundary row" shape BIO-CRD-006's own
    /// deny-list feed already established, so a caller re-polling with its own last-seen cursor
    /// never silently skips an entry, at the cost of occasionally re-receiving one it already
    /// has (upserting a revocation is a no-op: the transition table has no path back out of
    /// <see cref="PersonStatus.Revoked"/> except to <see cref="PersonStatus.Archived"/>, so this
    /// feed is monotonic per person once it has appeared).
    /// </summary>
    Task<IReadOnlyList<Person>> ListRevokedSinceAsync(string tenantId, string? since, CancellationToken cancellationToken = default);
}
