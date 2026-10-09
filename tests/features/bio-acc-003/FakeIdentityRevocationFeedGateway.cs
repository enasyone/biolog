using Biolog.Features.AccessCache.Domain;

namespace Biolog.Features.AccessCache.Tests;

/// <summary>Test double for <see cref="IIdentityRevocationFeedGateway"/> - stands in for the real PersonRegistry-backed adapter (`Biolog.Api.Modules.BioAcc003Module.RevocationFeedGatewayAdapter`), which needs a real ASP.NET host to construct.</summary>
public sealed class FakeIdentityRevocationFeedGateway : IIdentityRevocationFeedGateway
{
    private readonly List<RevocationEntry> _entriesByTenant = [];
    private readonly Dictionary<RevocationEntry, string> _tenantOf = [];

    public FakeIdentityRevocationFeedGateway Register(string tenantId, RevocationEntry entry)
    {
        _entriesByTenant.Add(entry);
        _tenantOf[entry] = tenantId;
        return this;
    }

    public Task<IReadOnlyList<RevocationEntry>> ListRevokedSinceAsync(string tenantId, string? since, CancellationToken cancellationToken = default)
    {
        IEnumerable<RevocationEntry> matches = _entriesByTenant.Where(e => _tenantOf[e] == tenantId);
        if (since is not null)
        {
            matches = matches.Where(e => string.CompareOrdinal(e.RevokedAt, since) >= 0);
        }

        IReadOnlyList<RevocationEntry> result = [.. matches.OrderBy(e => e.RevokedAt, StringComparer.Ordinal)];
        return Task.FromResult(result);
    }
}
