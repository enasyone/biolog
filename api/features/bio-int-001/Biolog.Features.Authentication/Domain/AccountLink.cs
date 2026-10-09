namespace Biolog.Features.Authentication;

/// <summary>
/// Binds a provider's own subject id to a BIOLOG person id, keyed by
/// (<see cref="TenantId"/>, <see cref="ProviderId"/>, <see cref="SubjectId"/>) - the first
/// successful authentication for that triple creates the link (audited as
/// <see cref="AuthAuditAction.AccountLinked"/> per BIO-INT-001-AC04); every later
/// authentication for the same triple reuses it. <see cref="PersonId"/> is a caller-supplied
/// or freshly generated reference, never resolved against BIO-IDN-001's own person registry -
/// this project cannot reference that feature project (same one-way-dependency boundary as
/// every other cross-project seam here), so binding a link's <see cref="PersonId"/> to a real,
/// existing BIOLOG person record is each consuming surface's own future integration step, the
/// same disclosed-scope shape BIO-RPT-001 already used for "no domain event publisher exists
/// yet" - honestly stated, not silently assumed away.
/// </summary>
public sealed record AccountLink(string TenantId, string ProviderId, string SubjectId, string PersonId, DateTimeOffset LinkedAt);

public interface IAccountLinkRepository
{
    Task<AccountLink?> FindAsync(string tenantId, string providerId, string subjectId, CancellationToken cancellationToken);

    /// <summary>Idempotent: linking an already-linked triple returns the EXISTING link
    /// unchanged rather than creating a second one or overwriting <see cref="AccountLink.PersonId"/>.</summary>
    Task<(AccountLink Link, bool WasCreated)> LinkAsync(
        string tenantId, string providerId, string subjectId, string personId, CancellationToken cancellationToken);
}

public sealed class InMemoryAccountLinkRepository : IAccountLinkRepository
{
    private readonly Dictionary<(string, string, string), AccountLink> _links = [];
    private readonly Lock _gate = new();

    public Task<AccountLink?> FindAsync(string tenantId, string providerId, string subjectId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_links.GetValueOrDefault((tenantId, providerId, subjectId)));
        }
    }

    public Task<(AccountLink Link, bool WasCreated)> LinkAsync(
        string tenantId, string providerId, string subjectId, string personId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = (tenantId, providerId, subjectId);
            if (_links.TryGetValue(key, out var existing))
            {
                return Task.FromResult((existing, false));
            }

            var link = new AccountLink(tenantId, providerId, subjectId, personId, DateTimeOffset.UtcNow);
            _links[key] = link;
            return Task.FromResult((link, true));
        }
    }
}
