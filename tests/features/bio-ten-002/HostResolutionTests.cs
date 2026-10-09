using Biolog.Features.TenantDomainRegistry.Api;
using Biolog.Features.TenantDomainRegistry.Audit;
using Biolog.Features.TenantDomainRegistry.Domain;
using Biolog.Features.TenantDomainRegistry.Resolution;
using Xunit;

namespace Biolog.Features.TenantDomainRegistry.Tests;

/// <summary>BIO-TEN-002-AC02 (case-normalised, exact) and AC03 (unverified/unknown fail closed).</summary>
public sealed class HostResolutionTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "admin_alice");
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";

    private static async Task<TenantDomainService> ServiceWithVerifiedDomain(string hostname, CancellationToken ct)
    {
        var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), new InMemoryTenantDomainAuditSink());
        var registered = await service.RegisterAsync(
            new RegisterDomainRequest { Hostname = hostname, TenantId = TenantId }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);
        await service.UpdateStatusAsync(
            registered.Hostname, new UpdateDomainStatusRequest { Version = registered.Version, Status = "VERIFIED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80", ct);
        return service;
    }

    [Fact]
    public async Task A_verified_host_resolves_to_its_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);

        var resolved = await service.ResolveAsync("portal.example.edu.ng", ct);

        Assert.Equal(TenantId, resolved.TenantId);
    }

    [Theory]
    [InlineData("PORTAL.EXAMPLE.EDU.NG")]
    [InlineData("Portal.Example.Edu.Ng")]
    public async Task Resolution_is_case_normalised(string variant)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);

        var resolved = await service.ResolveAsync(variant, ct);

        Assert.Equal("portal.example.edu.ng", resolved.Hostname);
    }

    [Fact]
    public async Task Resolution_strips_a_port_before_matching()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);

        var resolved = await service.ResolveAsync("portal.example.edu.ng:8443", ct);

        Assert.Equal("portal.example.edu.ng", resolved.Hostname);
    }

    // BIO-TEN-002-QA-02 (Seyi round 1, High): a colon must only ever remove a syntactically
    // valid terminal port - 1-5 ASCII digits, 0-65535 - never whatever text happens to follow
    // it. The original implementation split on the first colon unconditionally, so all three
    // of these malformed authorities silently aliased the verified hostname and resolved its
    // tenant; each must now be rejected outright, exactly like any other malformed hostname.
    [Theory]
    [InlineData("portal.example.edu.ng:evil")]
    [InlineData("portal.example.edu.ng:")]
    [InlineData("portal.example.edu.ng:443.attacker.invalid")]
    [InlineData("portal.example.edu.ng:-1")]
    [InlineData("portal.example.edu.ng:999999999999999")]
    [InlineData("portal.example.edu.ng:443:8443")]
    public async Task A_malformed_port_suffix_is_rejected_not_silently_stripped(string hostile)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.ResolveAsync(hostile, ct));

        Assert.Equal("host_not_recognised", error.Code); // never resolves the verified hostname it aliases toward
    }

    [Theory]
    [InlineData("portal.example.edu.ng:evil")]
    [InlineData("portal.example.edu.ng:")]
    [InlineData("portal.example.edu.ng:443.attacker.invalid")]
    [InlineData("portal.example.edu.ng:-1")]
    [InlineData("portal.example.edu.ng:65536")]
    public void Normalize_rejects_a_malformed_port_suffix_directly(string hostile)
    {
        Assert.Null(HostNormalizer.Normalize(hostile));
    }

    [Theory]
    [InlineData("portal.example.edu.ng:80")]
    [InlineData("portal.example.edu.ng:443")]
    [InlineData("portal.example.edu.ng:65535")]
    [InlineData("portal.example.edu.ng:0")]
    public void Normalize_accepts_a_syntactically_valid_port(string valid)
    {
        Assert.Equal("portal.example.edu.ng", HostNormalizer.Normalize(valid));
    }

    [Fact]
    public async Task Resolution_strips_one_trailing_dot_before_matching()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);

        var resolved = await service.ResolveAsync("portal.example.edu.ng.", ct);

        Assert.Equal("portal.example.edu.ng", resolved.Hostname);
    }

    [Fact]
    public async Task Resolution_is_exact_a_subdomain_of_a_verified_host_does_not_match()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await ServiceWithVerifiedDomain("example.edu.ng", ct);

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.ResolveAsync("portal.example.edu.ng", ct));

        Assert.Equal("host_not_recognised", error.Code);
    }

    [Fact]
    public async Task Resolution_is_exact_a_parent_domain_of_a_verified_host_does_not_match()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.ResolveAsync("example.edu.ng", ct));

        Assert.Equal("host_not_recognised", error.Code);
    }

    [Fact]
    public async Task An_unregistered_host_fails_closed_as_host_not_recognised()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), new InMemoryTenantDomainAuditSink());

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.ResolveAsync("nobody.example.edu.ng", ct));

        Assert.Equal("host_not_recognised", error.Code);
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task A_pending_verification_host_fails_closed_identically_to_an_unknown_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), new InMemoryTenantDomainAuditSink());
        await service.RegisterAsync(
            new RegisterDomainRequest { Hostname = "pending.example.edu.ng", TenantId = TenantId }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct);

        var unknownError = await Assert.ThrowsAsync<TenantDomainError>(() => service.ResolveAsync("nobody.example.edu.ng", ct));
        var pendingError = await Assert.ThrowsAsync<TenantDomainError>(() => service.ResolveAsync("pending.example.edu.ng", ct));

        // BIO-TEN-002-AC03 / ADR-0002 §1.3: identical code and status, on purpose - a caller
        // must not be able to distinguish "no such hostname" from "hostname exists but is not
        // verified yet" (that distinction is an enumeration oracle over the tenant registry).
        Assert.Equal(unknownError.Code, pendingError.Code);
        Assert.Equal(unknownError.Status, pendingError.Status);
    }

    [Fact]
    public async Task A_suspended_host_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);
        var current = await service.GetAsync("portal.example.edu.ng", ct);
        await service.UpdateStatusAsync(
            current!.Hostname, new UpdateDomainStatusRequest { Version = current.Version, Status = "SUSPENDED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.ResolveAsync("portal.example.edu.ng", ct));

        Assert.Equal("host_not_recognised", error.Code);
    }

    [Fact]
    public async Task A_retired_host_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await ServiceWithVerifiedDomain("portal.example.edu.ng", ct);
        var current = await service.GetAsync("portal.example.edu.ng", ct);
        await service.UpdateStatusAsync(
            current!.Hostname, new UpdateDomainStatusRequest { Version = current.Version, Status = "RETIRED" }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a81", ct);

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.ResolveAsync("portal.example.edu.ng", ct));

        Assert.Equal("host_not_recognised", error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_or_whitespace_host_fails_closed(string? rawHost)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), new InMemoryTenantDomainAuditSink());

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.ResolveAsync(rawHost, ct));

        Assert.Equal("host_not_recognised", error.Code);
    }

    [Fact]
    public async Task A_non_ascii_hostname_cannot_even_be_registered()
    {
        // Cyrillic а (U+0430) in place of Latin a - ADR-0002 §1.3's homoglyph example. Both
        // registration and resolution share HostNormalizer, so the homoglyph is rejected
        // before it can ever enter the store - a stronger guarantee than "rejected only at
        // resolve time": there is no record for an attacker's own lookup to ever race against.
        var ct = TestContext.Current.CancellationToken;
        var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), new InMemoryTenantDomainAuditSink());

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.RegisterAsync(
            new RegisterDomainRequest { Hostname = "pаypal.example", TenantId = TenantId }, Actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79", ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task A_non_ascii_host_at_resolve_time_fails_closed_even_if_never_registered()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), new InMemoryTenantDomainAuditSink());

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.ResolveAsync("pаypal.example", ct));

        Assert.Equal("host_not_recognised", error.Code);
    }

    [Fact]
    public void Normalize_rejects_non_ascii_directly()
    {
        Assert.Null(HostNormalizer.Normalize("pаypal.example"));
    }

    [Fact]
    public void Normalize_rejects_an_empty_label()
    {
        Assert.Null(HostNormalizer.Normalize("portal..example.edu.ng"));
    }

    [Fact]
    public void Normalize_rejects_a_hostname_over_253_characters()
    {
        var tooLong = string.Join('.', Enumerable.Repeat("abcdefghij", 26)); // 26*11 - 1 > 253
        Assert.Null(HostNormalizer.Normalize(tooLong));
    }
}
