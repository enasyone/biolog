using Biolog.Features.TenantDomainRegistry.Api;
using Biolog.Features.TenantDomainRegistry.Audit;
using Biolog.Features.TenantDomainRegistry.Domain;
using Biolog.Features.TenantDomainRegistry.Identifiers;
using Xunit;

namespace Biolog.Features.TenantDomainRegistry.Tests;

/// <summary>
/// ADR-0003 Decision 7's pattern, applied to BIO-TEN-002. Unlike BIO-TEN-001, this story does
/// not have to discover the pattern the hard way - it is now a codified, cross-cutting
/// requirement - but every element still needs its own proof against THIS aggregate's actual
/// shape, because Decision 7 was confirmed against single-record mutations and this story
/// introduces a genuinely two-record one (<see cref="TenantDomainService.SetPrimaryAsync"/>).
/// </summary>
public sealed class TenantDomainAuditTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "admin_alice");
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";

    private static RegisterDomainRequest ValidRequest(string hostname = "portal.example.edu.ng", string tenantId = TenantId) =>
        new() { Hostname = hostname, TenantId = tenantId };

    [Fact]
    public async Task Register_records_exactly_one_allowed_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryTenantDomainAuditSink();
        var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), sink);

        var domain = await service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("tenant_domain.register", evt.Action);
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
        Assert.Equal("tenant_domain", evt.Target.Type);
        Assert.Equal(domain.Hostname, evt.Target.Id);
        Assert.Null(evt.TenantId); // control-plane action (ADR-0002 §1.4 names domains explicitly)
    }

    [Fact]
    public async Task Verify_records_from_and_to_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryTenantDomainAuditSink();
        var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), sink);
        var domain = await service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        await service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "VERIFIED" }, Actor, CanonicalId.NewEventId(), ct);

        var evt = sink.Events.Single(e => e.Action == "tenant_domain.verify");
        Assert.Equal("PENDING_VERIFICATION", evt.Detail["from_status"]);
        Assert.Equal("VERIFIED", evt.Detail["to_status"]);
    }

    [Fact]
    public async Task SetPrimary_records_from_and_to_hostname()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryTenantDomainAuditSink();
        var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), sink);
        var a = await VerifiedDomain(service, "a.example.edu.ng", ct);
        var b = await VerifiedDomain(service, "b.example.edu.ng", ct);
        await service.SetPrimaryAsync(a.Hostname, new SetPrimaryDomainRequest { Version = a.Version }, Actor, CanonicalId.NewEventId(), ct);
        var currentB = await service.GetAsync(b.Hostname, ct);

        await service.SetPrimaryAsync(currentB!.Hostname, new SetPrimaryDomainRequest { Version = currentB.Version }, Actor, CanonicalId.NewEventId(), ct);

        var evt = sink.Events.Last(e => e.Action == "tenant_domain.primary_changed");
        Assert.Equal("a.example.edu.ng", evt.Detail["from_hostname"]);
        Assert.Equal("b.example.edu.ng", evt.Detail["to_hostname"]);
    }

    [Fact]
    public async Task A_failed_validation_records_no_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryTenantDomainAuditSink();
        var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), sink);

        await Assert.ThrowsAsync<TenantDomainError>(
            () => service.RegisterAsync(new RegisterDomainRequest { Hostname = "not a hostname", TenantId = TenantId }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task A_duplicate_hostname_attempt_records_no_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantDomainRegistry();
        var sink = new InMemoryTenantDomainAuditSink();
        var service = new TenantDomainService(registry, sink);
        await service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);
        var eventsAfterFirst = sink.Events.Count;

        await Assert.ThrowsAsync<TenantDomainError>(() => service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal(eventsAfterFirst, sink.Events.Count); // the rejected duplicate attempt added nothing
    }

    [Fact]
    public async Task Register_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantDomainRegistry();
        var service = new TenantDomainService(registry, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Null(await registry.GetByHostnameAsync("portal.example.edu.ng", ct)); // never reached registry.CreateAsync
    }

    [Fact]
    public async Task UpdateStatus_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantDomainRegistry();
        var domain = await new TenantDomainService(registry, new InMemoryTenantDomainAuditSink())
            .RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);
        var service = new TenantDomainService(registry, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "VERIFIED" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        var stillStored = await registry.GetByHostnameAsync(domain.Hostname, ct);
        Assert.Equal(TenantDomainStatus.PendingVerification, stillStored!.Status); // untouched
        Assert.Equal(domain.Version, stillStored.Version);
    }

    [Fact]
    public async Task SetPrimary_is_never_committed_when_the_audit_sink_fails_and_neither_sibling_is_touched()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantDomainRegistry();
        var plainSink = new InMemoryTenantDomainAuditSink();
        var seedService = new TenantDomainService(registry, plainSink);
        var a = await VerifiedDomain(seedService, "a.example.edu.ng", ct);
        var b = await VerifiedDomain(seedService, "b.example.edu.ng", ct);
        await seedService.SetPrimaryAsync(a.Hostname, new SetPrimaryDomainRequest { Version = a.Version }, Actor, CanonicalId.NewEventId(), ct);
        var currentA = (await registry.GetByHostnameAsync(a.Hostname, ct))!;
        var currentB = (await registry.GetByHostnameAsync(b.Hostname, ct))!;

        var service = new TenantDomainService(registry, new ThrowingAuditSink());
        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.SetPrimaryAsync(
            currentB.Hostname, new SetPrimaryDomainRequest { Version = currentB.Version }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.True((await registry.GetByHostnameAsync(a.Hostname, ct))!.IsPrimary); // A is still primary
        Assert.False((await registry.GetByHostnameAsync(b.Hostname, ct))!.IsPrimary); // B was never flipped on
    }

    [Fact]
    public async Task Two_genuinely_concurrent_status_updates_of_the_same_domain_never_produce_a_false_allowed_audit()
    {
        // Reproduces BIO-TEN-001's round-3 proof against this story's own registry: two
        // "concurrent" callers of the SAME hostname must fully serialize under the tenant
        // lock, and the loser's rejected commit must never be described by a durably-ALLOWED
        // audit event. The seed create below uses a SEPARATE service over a plain sink -
        // installing the delaying sink before setup would consume its one forced delay on
        // setup and prove nothing about real contention (the exact bug BIO-TEN-001 round 4
        // found in its own first attempt at this test).
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantDomainRegistry();
        var plainSink = new InMemoryTenantDomainAuditSink();
        var domain = await new TenantDomainService(registry, plainSink).RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);
        var verified = await new TenantDomainService(registry, plainSink).UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "VERIFIED" }, Actor, CanonicalId.NewEventId(), ct);

        var delayingSink = new DelayFirstCallAuditSink(plainSink);
        var service = new TenantDomainService(registry, delayingSink); // one shared instance = one shared lock table

        var taskA = TryRunAsync(() => service.UpdateStatusAsync(
            verified.Hostname, new UpdateDomainStatusRequest { Version = verified.Version, Status = "SUSPENDED" }, Actor, CanonicalId.NewEventId(), ct));
        var taskB = TryRunAsync(() => service.UpdateStatusAsync(
            verified.Hostname, new UpdateDomainStatusRequest { Version = verified.Version, Status = "RETIRED" }, Actor, CanonicalId.NewEventId(), ct));

        var (resultA, errorA) = await taskA;
        var (resultB, errorB) = await taskB;

        var winner = resultA ?? resultB;
        var loserError = errorA ?? errorB;
        Assert.NotNull(winner);
        Assert.NotNull(loserError);
        Assert.Equal("concurrency_conflict", loserError!.Code);

        var statusChangeEvents = plainSink.Events.Where(e => e.Action is "tenant_domain.suspend" or "tenant_domain.retire").ToList();
        var evt = Assert.Single(statusChangeEvents);
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);

        var stored = await registry.GetByHostnameAsync(verified.Hostname, ct);
        Assert.Equal(winner!.Status, stored!.Status);
    }

    [Fact]
    public async Task SetPrimary_on_two_different_domains_of_the_same_tenant_never_leaves_two_domains_primary()
    {
        // The novel risk this story adds beyond BIO-TEN-001's pattern: SetPrimaryAsync spans
        // TWO records, so its real aggregate is "this tenant's domain set", not one domain
        // (class header, "Two lock scopes, not one"). If verify/suspend/retire/set-primary
        // used a NARROWER, per-hostname lock instead of the shared per-tenant one, two
        // concurrent SetPrimary calls for DIFFERENT domains of the SAME tenant could each
        // read "no current primary" before either commits, and both durably become primary -
        // no CAS conflict at all, because each only ever touches its own record's key. That
        // would violate "at most one primary per tenant" with no error raised anywhere. This
        // test proves the shared tenant-scoped lock actually prevents that.
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantDomainRegistry();
        var plainSink = new InMemoryTenantDomainAuditSink();
        var seedService = new TenantDomainService(registry, plainSink);
        var a = await VerifiedDomain(seedService, "a.example.edu.ng", ct);
        var b = await VerifiedDomain(seedService, "b.example.edu.ng", ct);

        var delayingSink = new DelayFirstCallAuditSink(plainSink);
        var service = new TenantDomainService(registry, delayingSink);

        var taskA = service.SetPrimaryAsync(a.Hostname, new SetPrimaryDomainRequest { Version = a.Version }, Actor, CanonicalId.NewEventId(), ct);
        var taskB = service.SetPrimaryAsync(b.Hostname, new SetPrimaryDomainRequest { Version = b.Version }, Actor, CanonicalId.NewEventId(), ct);
        await Task.WhenAll(taskA, taskB);

        var finalA = await registry.GetByHostnameAsync(a.Hostname, ct);
        var finalB = await registry.GetByHostnameAsync(b.Hostname, ct);

        Assert.NotEqual(finalA!.IsPrimary, finalB!.IsPrimary); // exactly one of the two, never both, never neither
    }

    [Fact]
    public async Task An_audit_write_that_durably_lands_but_throws_on_acknowledgement_still_commits_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantDomainRegistry();
        var sink = new InMemoryTenantDomainAuditSink();
        var domain = await new TenantDomainService(registry, sink).RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        var service = new TenantDomainService(registry, new DurablyWritesThenThrowsAuditSink(sink));
        var updated = await service.UpdateStatusAsync(
            domain.Hostname, new UpdateDomainStatusRequest { Version = domain.Version, Status = "VERIFIED" }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(TenantDomainStatus.Verified, updated.Status); // succeeded, not audit_unavailable
        var stored = await registry.GetByHostnameAsync(domain.Hostname, ct);
        Assert.Equal(updated, stored);
    }

    [Fact]
    public async Task A_total_audit_store_outage_where_even_verification_fails_still_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<TenantDomainError>(() => service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
    }

    [Fact]
    public async Task A_cancellation_arriving_right_after_the_audit_write_succeeds_does_not_abort_the_commit()
    {
        // Built in from the start (ADR-0003 Decision 7, element 5), not discovered later the
        // way BIO-TEN-001 had to - but still proven directly against this story's own commit
        // path, not merely asserted by citing the ADR.
        var ct = TestContext.Current.CancellationToken;
        using var cts = new CancellationTokenSource();
        var registry = new ThrowsOnCancelledTokenRegistry(new InMemoryTenantDomainRegistry());
        var sink = new CancelsCallerTokenAfterWriteAuditSink(new InMemoryTenantDomainAuditSink(), cts);
        var service = new TenantDomainService(registry, sink);

        var domain = await service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), cts.Token);

        Assert.True(cts.IsCancellationRequested);
        Assert.NotNull(await registry.GetByHostnameAsync(domain.Hostname, ct));
    }

    private static async Task<Domain.TenantDomain> VerifiedDomain(TenantDomainService service, string hostname, CancellationToken ct)
    {
        var registered = await service.RegisterAsync(new RegisterDomainRequest { Hostname = hostname, TenantId = TenantId }, Actor, CanonicalId.NewEventId(), ct);
        return await service.UpdateStatusAsync(
            registered.Hostname, new UpdateDomainStatusRequest { Version = registered.Version, Status = "VERIFIED" }, Actor, CanonicalId.NewEventId(), ct);
    }

    private static async Task<(Domain.TenantDomain? Result, TenantDomainError? Error)> TryRunAsync(Func<Task<Domain.TenantDomain>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (TenantDomainError ex)
        {
            return (null, ex);
        }
    }

    private sealed class ThrowingAuditSink : ITenantDomainAuditSink
    {
        public Task WriteAsync(TenantDomainAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage: cannot even verify");
    }

    private sealed class DelayFirstCallAuditSink(ITenantDomainAuditSink inner) : ITenantDomainAuditSink
    {
        private int _calls;

        public async Task WriteAsync(TenantDomainAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }

            await inner.WriteAsync(auditEvent, cancellationToken);
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    private sealed class DurablyWritesThenThrowsAuditSink(ITenantDomainAuditSink inner) : ITenantDomainAuditSink
    {
        public async Task WriteAsync(TenantDomainAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            throw new InvalidOperationException("synthetic ack failure after a successful durable write");
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    private sealed class CancelsCallerTokenAfterWriteAuditSink(ITenantDomainAuditSink inner, CancellationTokenSource cts) : ITenantDomainAuditSink
    {
        public async Task WriteAsync(TenantDomainAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            cts.Cancel();
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    private sealed class ThrowsOnCancelledTokenRegistry(ITenantDomainRegistry inner) : ITenantDomainRegistry
    {
        public Task<Domain.TenantDomain> CreateAsync(Domain.TenantDomain domain, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.CreateAsync(domain, cancellationToken);
        }

        public Task<Domain.TenantDomain?> GetByHostnameAsync(string hostname, CancellationToken cancellationToken = default) =>
            inner.GetByHostnameAsync(hostname, cancellationToken);

        public Task<IReadOnlyList<Domain.TenantDomain>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
            inner.ListByTenantAsync(tenantId, cancellationToken);

        public Task<Domain.TenantDomain> ReplaceAsync(Domain.TenantDomain expected, Domain.TenantDomain updated, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.ReplaceAsync(expected, updated, cancellationToken);
        }
    }
}
