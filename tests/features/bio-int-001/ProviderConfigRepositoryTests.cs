using Biolog.Features.Authentication;
using Xunit;

namespace Biolog.Features.Authentication.Tests;

/// <summary>BIO-INT-001-AC01: provider configuration is tenant-scoped.</summary>
public sealed class ProviderConfigRepositoryTests
{
    private static AuthProviderRegistrationRequest Request(AuthProviderType type = AuthProviderType.Password) =>
        new(type, "Test provider", MfaRequirement.None, []);

    [Fact]
    public async Task A_tenant_cannot_read_another_tenants_provider_by_id()
    {
        var repo = new InMemoryAuthProviderConfigRepository();
        var config = await repo.CreateAsync("ten_a", Request(), CancellationToken.None);

        var seenByOwner = await repo.GetAsync("ten_a", config.ProviderId, CancellationToken.None);
        var seenByOther = await repo.GetAsync("ten_b", config.ProviderId, CancellationToken.None);

        Assert.NotNull(seenByOwner);
        Assert.Null(seenByOther); // not found, never forbidden (ADR-0002 §4.3)
    }

    [Fact]
    public async Task Listing_only_returns_the_calling_tenants_own_providers()
    {
        var repo = new InMemoryAuthProviderConfigRepository();
        await repo.CreateAsync("ten_a", Request(), CancellationToken.None);
        await repo.CreateAsync("ten_a", Request(), CancellationToken.None);
        await repo.CreateAsync("ten_b", Request(), CancellationToken.None);

        var listA = await repo.ListAsync("ten_a", CancellationToken.None);
        var listB = await repo.ListAsync("ten_b", CancellationToken.None);

        Assert.Equal(2, listA.Count);
        Assert.Single(listB);
    }

    [Fact]
    public async Task ListEnabled_excludes_a_disabled_provider()
    {
        var repo = new InMemoryAuthProviderConfigRepository();
        var config = await repo.CreateAsync("ten_a", Request(), CancellationToken.None);
        await repo.UpdateStatusAsync("ten_a", config.ProviderId, new AuthProviderStatusChangeRequest(false, 1), CancellationToken.None);

        var enabled = await repo.ListEnabledAsync("ten_a", CancellationToken.None);

        Assert.Empty(enabled);
    }

    [Fact]
    public async Task Status_change_with_a_stale_expected_version_throws_a_concurrency_conflict()
    {
        var repo = new InMemoryAuthProviderConfigRepository();
        var config = await repo.CreateAsync("ten_a", Request(), CancellationToken.None);

        await Assert.ThrowsAsync<AuthProviderConcurrencyConflictException>(() =>
            repo.UpdateStatusAsync("ten_a", config.ProviderId, new AuthProviderStatusChangeRequest(false, 999), CancellationToken.None));
    }

    [Fact]
    public async Task A_tenant_cannot_disable_another_tenants_provider()
    {
        var repo = new InMemoryAuthProviderConfigRepository();
        var config = await repo.CreateAsync("ten_a", Request(), CancellationToken.None);

        var result = await repo.UpdateStatusAsync("ten_b", config.ProviderId, new AuthProviderStatusChangeRequest(false, 1), CancellationToken.None);

        Assert.Null(result);
        var stillEnabled = await repo.GetAsync("ten_a", config.ProviderId, CancellationToken.None);
        Assert.True(stillEnabled!.Enabled);
    }
}
