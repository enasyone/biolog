using Biolog.Features.BiometricRetention.Domain;
using Xunit;

namespace Biolog.Features.BiometricRetention.Tests;

public sealed class InMemoryRawImageRetentionPolicyTests
{
    [Fact]
    public async Task AC03_a_tenant_that_has_never_been_authorised_defaults_to_not_authorised()
    {
        var policy = new InMemoryRawImageRetentionPolicy();

        var authorised = await policy.IsAuthorisedAsync("ten_a", CancellationToken.None);

        Assert.False(authorised);
    }

    [Fact]
    public async Task AC03_authorising_a_tenant_makes_it_report_authorised()
    {
        var policy = new InMemoryRawImageRetentionPolicy();
        await policy.AuthoriseAsync("ten_a", CancellationToken.None);

        var authorised = await policy.IsAuthorisedAsync("ten_a", CancellationToken.None);

        Assert.True(authorised);
    }

    [Fact]
    public async Task AC03_authorising_one_tenant_does_not_affect_another_tenant()
    {
        var policy = new InMemoryRawImageRetentionPolicy();
        await policy.AuthoriseAsync("ten_a", CancellationToken.None);

        var forTenantB = await policy.IsAuthorisedAsync("ten_b", CancellationToken.None);

        Assert.False(forTenantB);
    }

    [Fact]
    public async Task AC03_a_previously_authorised_tenant_can_have_authorisation_revoked()
    {
        var policy = new InMemoryRawImageRetentionPolicy();
        await policy.AuthoriseAsync("ten_a", CancellationToken.None);
        await policy.RevokeAsync("ten_a", CancellationToken.None);

        var authorised = await policy.IsAuthorisedAsync("ten_a", CancellationToken.None);

        Assert.False(authorised);
    }
}
