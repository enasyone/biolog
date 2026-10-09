using Biolog.Features.TenantDomainRegistry.Api;
using Biolog.Features.TenantDomainRegistry.Audit;
using Biolog.Features.TenantDomainRegistry.Domain;
using Xunit;

namespace Biolog.Features.TenantDomainRegistry.Tests;

/// <summary>BIO-TEN-002-AC01: TenantDomain CRUD/lifecycle behaviour.</summary>
public sealed class TenantDomainServiceTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "admin_alice");
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a78";

    private static TenantDomainService NewService() =>
        new(new InMemoryTenantDomainRegistry(), new InMemoryTenantDomainAuditSink());

    private static RegisterDomainRequest ValidRequest(string hostname = "portal.example.edu.ng", string tenantId = TenantA) =>
        new() { Hostname = hostname, TenantId = tenantId };

    [Fact]
    public async Task Register_creates_a_pending_verification_domain_with_no_primary_flag()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var domain = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal("portal.example.edu.ng", domain.Hostname);
        Assert.Equal(TenantA, domain.TenantId);
        Assert.Equal(TenantDomainStatus.PendingVerification, domain.Status);
        Assert.False(domain.IsPrimary);
        Assert.Equal(1, domain.Version);
    }

    [Fact]
    public async Task Register_lowercases_the_hostname_it_stores()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var domain = await service.RegisterAsync(ValidRequest(hostname: "Portal.Example.EDU.NG"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        Assert.Equal("portal.example.edu.ng", domain.Hostname);
    }

    [Fact]
    public async Task Register_rejects_a_malformed_hostname()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<TenantDomainError>(
            () => service.RegisterAsync(ValidRequest(hostname: "not a hostname!!"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Register_rejects_a_malformed_tenant_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<TenantDomainError>(
            () => service.RegisterAsync(ValidRequest(tenantId: "not-a-tenant-id"), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Register_rejects_a_hostname_already_claimed_by_another_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        await service.RegisterAsync(ValidRequest(tenantId: TenantA), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<TenantDomainError>(
            () => service.RegisterAsync(ValidRequest(tenantId: TenantB), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));

        Assert.Equal("duplicate_hostname", error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public async Task Get_normalises_the_lookup_hostname()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var domain = await service.GetAsync("PORTAL.EXAMPLE.EDU.NG", ct);

        Assert.NotNull(domain);
        Assert.Equal("portal.example.edu.ng", domain!.Hostname);
    }

    [Fact]
    public async Task ListByTenant_returns_only_that_tenants_domains()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        await service.RegisterAsync(ValidRequest(hostname: "a.example.edu.ng", tenantId: TenantA), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        await service.RegisterAsync(ValidRequest(hostname: "b.example.edu.ng", tenantId: TenantB), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var domains = await service.ListByTenantAsync(TenantA, ct);

        var domain = Assert.Single(domains);
        Assert.Equal("a.example.edu.ng", domain.Hostname);
    }

    [Fact]
    public async Task Verify_transitions_pending_verification_to_verified()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domain = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var verified = await service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "VERIFIED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        Assert.Equal(TenantDomainStatus.Verified, verified.Status);
        Assert.Equal(domain.Version + 1, verified.Version);
    }

    [Fact]
    public async Task Suspend_requires_a_verified_domain_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domain = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "SUSPENDED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal("invalid_status_transition", error.Errors!.Single().Code);
    }

    [Fact]
    public async Task Suspend_from_verified_succeeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domain = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        var verified = await service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "VERIFIED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var suspended = await service.UpdateStatusAsync(
            verified.Hostname, new UpdateDomainStatusRequest { Version = verified.Version, Status = "SUSPENDED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);

        Assert.Equal(TenantDomainStatus.Suspended, suspended.Status);
    }

    [Fact]
    public async Task Retire_is_reachable_directly_from_pending_verification()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domain = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var retired = await service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "RETIRED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        Assert.Equal(TenantDomainStatus.Retired, retired.Status);
    }

    [Fact]
    public async Task Retire_is_reachable_from_verified()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domain = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        var verified = await service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "VERIFIED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var retired = await service.UpdateStatusAsync(
            verified.Hostname, new UpdateDomainStatusRequest { Version = verified.Version, Status = "RETIRED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);

        Assert.Equal(TenantDomainStatus.Retired, retired.Status);
    }

    [Fact]
    public async Task Retire_is_reachable_from_suspended()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domain = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        var verified = await service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "VERIFIED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);
        var suspended = await service.UpdateStatusAsync(
            verified.Hostname, new UpdateDomainStatusRequest { Version = verified.Version, Status = "SUSPENDED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);

        var retired = await service.UpdateStatusAsync(
            suspended.Hostname, new UpdateDomainStatusRequest { Version = suspended.Version, Status = "RETIRED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a82", ct);

        Assert.Equal(TenantDomainStatus.Retired, retired.Status);
    }

    [Fact]
    public async Task Retire_is_a_no_op_when_already_retired()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domain = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        var retired = await service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "RETIRED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var again = await service.UpdateStatusAsync(
            retired.Hostname, new UpdateDomainStatusRequest { Version = retired.Version, Status = "RETIRED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);

        Assert.Equal(retired.Version, again.Version); // no-op: version did not move
    }

    [Fact]
    public async Task No_reinstate_transition_exists_from_retired()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domain = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        var retired = await service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "RETIRED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.UpdateStatusAsync(
            retired.Hostname, new UpdateDomainStatusRequest { Version = retired.Version, Status = "VERIFIED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct));

        Assert.Equal("invalid_status_transition", error.Errors!.Single().Code);
    }

    [Fact]
    public async Task SetPrimary_requires_a_verified_domain()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domain = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.SetPrimaryAsync(
            domain.Hostname, new SetPrimaryDomainRequest { Version = domain.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task SetPrimary_flips_the_old_primary_off_and_the_new_one_on()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domainA = await VerifiedDomain(service, "a.example.edu.ng", TenantA, ct, 1);
        var domainB = await VerifiedDomain(service, "b.example.edu.ng", TenantA, ct, 10);

        var primaryA = await service.SetPrimaryAsync(
            domainA.Hostname, new SetPrimaryDomainRequest { Version = domainA.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a20", ct);
        Assert.True(primaryA.IsPrimary);

        var primaryB = await service.SetPrimaryAsync(
            domainB.Hostname, new SetPrimaryDomainRequest { Version = domainB.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a21", ct);
        Assert.True(primaryB.IsPrimary);

        var refreshedA = await service.GetAsync(domainA.Hostname, ct);
        Assert.False(refreshedA!.IsPrimary); // A was automatically unset when B became primary
    }

    [Fact]
    public async Task SetPrimary_is_a_no_op_when_already_primary()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domain = await VerifiedDomain(service, "a.example.edu.ng", TenantA, ct, 1);
        var primary = await service.SetPrimaryAsync(
            domain.Hostname, new SetPrimaryDomainRequest { Version = domain.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a20", ct);

        var again = await service.SetPrimaryAsync(
            primary.Hostname, new SetPrimaryDomainRequest { Version = primary.Version }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a21", ct);

        Assert.Equal(primary.Version, again.Version); // no-op: version did not move
    }

    [Fact]
    public async Task A_stale_version_on_status_update_is_a_concurrency_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var domain = await service.RegisterAsync(ValidRequest(), Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version + 1, Status = "VERIFIED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    private static async Task<Domain.TenantDomain> VerifiedDomain(
        TenantDomainService service, string hostname, string tenantId, CancellationToken ct, int correlationSeed)
    {
        var registered = await service.RegisterAsync(
            new RegisterDomainRequest { Hostname = hostname, TenantId = tenantId }, Actor, $"evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a{correlationSeed:D2}", ct);
        return await service.UpdateStatusAsync(
            registered.Hostname, new UpdateDomainStatusRequest { Version = registered.Version, Status = "VERIFIED" }, Actor, $"evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a{correlationSeed + 1:D2}", ct);
    }
}
