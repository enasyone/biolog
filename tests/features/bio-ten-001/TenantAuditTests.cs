using Biolog.Features.TenantRegistry;
using Biolog.Features.TenantRegistry.Api;
using Biolog.Features.TenantRegistry.Audit;
using Biolog.Features.TenantRegistry.Domain;
using Biolog.Features.TenantRegistry.Identifiers;
using Xunit;

namespace Biolog.Features.TenantRegistry.Tests;

/// <summary>BIO-TEN-001-AC04: audit events record administrative changes.</summary>
public sealed class TenantAuditTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "admin_alice");

    private static CreateTenantRequest ValidCreateRequest(string name = "Acme University") => new()
    {
        Name = name,
        LicensePlan = "enterprise",
        LicenseSeats = 500,
        RoutingRegion = "eu-west",
    };

    [Fact]
    public async Task Create_records_exactly_one_allowed_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryTenantAuditSink();
        var service = new TenantService(new InMemoryTenantRegistry(), sink);
        var correlationId = CanonicalId.NewEventId();

        var tenant = await service.CreateAsync(ValidCreateRequest(), Actor, correlationId, ct);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("tenant.create", evt.Action);
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
        Assert.Equal("tenant", evt.Target.Type);
        Assert.Equal(tenant.Id, evt.Target.Id);
        Assert.Null(evt.TenantId); // control-plane action: never a tenant-scoped event (ADR-0002 §1.4)
        Assert.Equal(correlationId, evt.CorrelationId);
        Assert.Equal(Actor, evt.Actor);
        Assert.Equal("tenant_created", evt.ReasonCode);
        Assert.True(CanonicalId.IsEventId(evt.EventId));
    }

    [Fact]
    public async Task Update_records_an_audit_event_only_when_something_actually_changed()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryTenantAuditSink();
        var service = new TenantService(new InMemoryTenantRegistry(), sink);
        var tenant = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);
        Assert.Single(sink.Events); // baseline: 1 event so far (create)

        // No-op update: same values as already stored.
        await service.UpdateAsync(
            tenant.Id,
            new UpdateTenantRequest { Version = tenant.Version, Name = tenant.Name },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.Single(sink.Events); // still just the create event - nothing changed

        await service.UpdateAsync(
            tenant.Id,
            new UpdateTenantRequest { Version = tenant.Version, Name = "A Different Name" },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(2, sink.Events.Count);
        Assert.Equal("tenant.update", sink.Events[1].Action);
        Assert.Equal("A Different Name", sink.Events[1].Detail["name"]);
    }

    [Fact]
    public async Task StatusChange_records_from_and_to_status_in_the_detail()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryTenantAuditSink();
        var service = new TenantService(new InMemoryTenantRegistry(), sink);
        var tenant = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        await service.UpdateStatusAsync(
            tenant.Id, new UpdateTenantStatusRequest { Version = tenant.Version, Status = "suspended" }, Actor, CanonicalId.NewEventId(), ct);

        var evt = sink.Events.Single(e => e.Action == "tenant.status_change");
        Assert.Equal("active", evt.Detail["from_status"]);
        Assert.Equal("suspended", evt.Detail["to_status"]);
        Assert.Equal("tenant_status_changed", evt.ReasonCode);
    }

    [Fact]
    public async Task SoftDelete_records_a_tenant_delete_action()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryTenantAuditSink();
        var service = new TenantService(new InMemoryTenantRegistry(), sink);
        var tenant = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        await service.SoftDeleteAsync(tenant.Id, tenant.Version, Actor, CanonicalId.NewEventId(), ct);

        Assert.Contains(sink.Events, e => e.Action == "tenant.delete" && e.ReasonCode == "tenant_disabled");
    }

    [Fact]
    public async Task A_failed_validation_records_no_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryTenantAuditSink();
        var service = new TenantService(new InMemoryTenantRegistry(), sink);

        await Assert.ThrowsAsync<TenantRegistryError>(
            () => service.CreateAsync(ValidCreateRequest(name: ""), Actor, CanonicalId.NewEventId(), ct));

        Assert.Empty(sink.Events); // nothing changed, nothing to audit
    }

    // BIO-TEN-001-QA-01 (Seyi review, two rounds). Round 1 committed the mutation before
    // auditing and rolled back on failure; round 1's re-review found the rollback itself
    // was not concurrency-safe (a writer that committed after the failing writer, but
    // before its rollback CAS ran, could permanently absorb the failing writer's unaudited
    // field with no event ever describing it - see TenantService's class header). The fix
    // audits BEFORE committing, so there is never a committed value to roll back. The four
    // tests below prove "never committed" (not "committed then undone") for each mutation,
    // and the fifth reproduces Seyi's exact writer-A/writer-B interleaving to prove that
    // scenario can no longer happen.

    [Fact]
    public async Task Create_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantRegistry();
        var service = new TenantService(registry, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<TenantRegistryError>(
            () => service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Equal(503, error.Status);

        var (allTenants, _) = await registry.ListAsync(limit: 50, afterId: null, statusFilter: null, ct);
        Assert.Empty(allTenants); // never reached registry.CreateAsync
    }

    [Fact]
    public async Task Update_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantRegistry();
        var tenant = await new TenantService(registry, new InMemoryTenantAuditSink())
            .CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);
        var service = new TenantService(registry, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<TenantRegistryError>(() => service.UpdateAsync(
            tenant.Id, new UpdateTenantRequest { Version = tenant.Version, Name = "Changed Name" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);

        var stillStored = await registry.GetAsync(tenant.Id, ct);
        Assert.Equal(tenant, stillStored); // untouched: never reached registry.ReplaceAsync
    }

    [Fact]
    public async Task StatusChange_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantRegistry();
        var tenant = await new TenantService(registry, new InMemoryTenantAuditSink())
            .CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);
        var service = new TenantService(registry, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<TenantRegistryError>(() => service.UpdateStatusAsync(
            tenant.Id, new UpdateTenantStatusRequest { Version = tenant.Version, Status = "suspended" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);

        var stillStored = await registry.GetAsync(tenant.Id, ct);
        Assert.Equal(TenantStatus.Active, stillStored!.Status); // untouched, not left suspended
        Assert.Equal(tenant.Version, stillStored.Version);
    }

    [Fact]
    public async Task SoftDelete_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantRegistry();
        var tenant = await new TenantService(registry, new InMemoryTenantAuditSink())
            .CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);
        var service = new TenantService(registry, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<TenantRegistryError>(
            () => service.SoftDeleteAsync(tenant.Id, tenant.Version, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);

        var stillStored = await registry.GetAsync(tenant.Id, ct);
        Assert.Equal(TenantStatus.Active, stillStored!.Status); // untouched, not left disabled
        Assert.Equal(tenant.Version, stillStored.Version);
    }

    [Fact]
    public async Task A_failing_writers_field_never_survives_in_a_later_writers_committed_version()
    {
        // Seyi's exact reproduction (REREVIEW.md): writer A changes `name` and hits a
        // failing audit sink; writer B then changes `license_seats` and succeeds. Round 1's
        // rollback-after-commit design let A's name change survive inside B's committed
        // version with no audit event ever naming it. Audit-before-commit makes this
        // impossible: A's change never reaches the registry, so B reads the ORIGINAL name.
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantRegistry();
        var sink = new InMemoryTenantAuditSink();
        var tenant = await new TenantService(registry, sink).CreateAsync(ValidCreateRequest(name: "Original Name"), Actor, CanonicalId.NewEventId(), ct);

        var writerA = new TenantService(registry, new ThrowingAuditSink());
        var errorA = await Assert.ThrowsAsync<TenantRegistryError>(() => writerA.UpdateAsync(
            tenant.Id, new UpdateTenantRequest { Version = tenant.Version, Name = "Writer A's Unaudited Name" }, Actor, CanonicalId.NewEventId(), ct));
        Assert.Equal("audit_unavailable", errorA.Code);

        var writerB = new TenantService(registry, sink);
        var afterB = await writerB.UpdateAsync(
            tenant.Id, new UpdateTenantRequest { Version = tenant.Version, LicenseSeats = 999 }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal("Original Name", afterB.Name); // A's failed change never landed, in B's version or any other
        Assert.Equal(999, afterB.LicenseSeats); // B's own, audited change did land
        Assert.DoesNotContain(sink.Events, e => // no event anywhere ever asserts writer A's attempted value
            e.Detail.TryGetValue("name", out var name) && Equals(name, "Writer A's Unaudited Name"));
    }

    [Fact]
    public async Task Two_genuinely_concurrent_writers_of_the_same_tenant_never_produce_a_false_allowed_audit_for_a_rejected_commit()
    {
        // BIO-TEN-001-QA-01 round 3 (REREVIEW-ROUND3.md): audit-before-commit closed round
        // 2's race, but on its own it can durably record Outcome = Allowed for a mutation
        // whose CAS then loses to a genuinely concurrent writer of the SAME tenant - a false
        // "this succeeded" in the permanent audit trail. The per-tenant lock closes this:
        // two concurrent UpdateAsync calls against the SAME (singleton) TenantService
        // instance for the SAME tenant fully serialize.
        //
        // DelayFirstCallAuditSink forces genuine interleaving: without it, every operation
        // here completes synchronously (no real async yield anywhere in the in-memory
        // stack), so writer B would never actually attempt to run until writer A had already
        // fully finished - which would prove nothing about the lock, only that the ordinary
        // version check works. Delaying the FIRST audit write gives writer B's call a real
        // chance to start and hit the (already held) per-tenant lock while writer A is still
        // inside it, which is exactly the interleaving round 3 was reported against.
        //
        // Round 4 re-review (REREVIEW-ROUND4.md) caught a real bug here: the delaying sink
        // was installed BEFORE the seed CreateAsync call, so its one delay was consumed by
        // setup and neither writer A nor writer B ever actually contended for the lock - the
        // test passed for the wrong reason (sequential execution, not proven serialization).
        // The seed create below uses a separate service over a PLAIN sink; only the service
        // shared by writer A and writer B uses the delaying one.
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantRegistry();
        var sink = new InMemoryTenantAuditSink();
        var tenant = await new TenantService(registry, sink).CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        var delayingSink = new DelayFirstCallAuditSink(sink);
        var service = new TenantService(registry, delayingSink); // one shared instance = one shared lock table

        // Both "writers" observed the same starting version, exactly as two real concurrent
        // HTTP requests reading the same tenant before either one's write would.
        var taskA = TryRunAsync(() => service.UpdateAsync(
            tenant.Id, new UpdateTenantRequest { Version = tenant.Version, Name = "Writer A Name" }, Actor, CanonicalId.NewEventId(), ct));
        var taskB = TryRunAsync(() => service.UpdateAsync(
            tenant.Id, new UpdateTenantRequest { Version = tenant.Version, LicenseSeats = 777 }, Actor, CanonicalId.NewEventId(), ct));

        var (resultA, errorA) = await taskA;
        var (resultB, errorB) = await taskB;

        // Exactly one of the two wins (regardless of which); the other is rejected as a
        // stale write, not by a phantom rollback or a false audit trail.
        var winner = resultA ?? resultB;
        var loserError = errorA ?? errorB;
        Assert.NotNull(winner);
        Assert.NotNull(loserError);
        Assert.Equal("concurrency_conflict", loserError!.Code);

        // Exactly one audit event exists for this update - the winner's - and it is honest:
        // the registry's actual final state matches it exactly.
        var updateEvents = sink.Events.Where(e => e.Action == "tenant.update").ToList();
        var evt = Assert.Single(updateEvents);
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);

        var stored = await registry.GetAsync(tenant.Id, ct);
        Assert.Equal(winner!.Version, stored!.Version);
        Assert.Equal(winner.Name, stored.Name);
        Assert.Equal(winner.LicenseSeats, stored.LicenseSeats);
    }

    [Fact]
    public async Task An_audit_write_that_durably_lands_but_throws_on_acknowledgement_still_commits_correctly()
    {
        // BIO-TEN-001-QA-01 round 4 finding #1 (REREVIEW-ROUND4.md): WriteAsync throwing
        // does not prove the event never landed - a real store can commit and then fail to
        // acknowledge. Round 3's code treated every throw as "definitely failed" and would
        // have left this durably-written ALLOWED event with no matching commit (a false-
        // success record). AuditAsync now verifies via ExistsAsync before deciding, so this
        // ambiguous case resolves to "the event is real, so make the commit real too."
        var ct = TestContext.Current.CancellationToken;
        var registry = new InMemoryTenantRegistry();
        var sink = new InMemoryTenantAuditSink();
        var tenant = await new TenantService(registry, sink).CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct);

        var service = new TenantService(registry, new DurablyWritesThenThrowsAuditSink(sink));
        var updated = await service.UpdateAsync(
            tenant.Id, new UpdateTenantRequest { Version = tenant.Version, Name = "Changed Despite Ack Failure" }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal("Changed Despite Ack Failure", updated.Name); // the operation succeeded, not audit_unavailable

        var evt = sink.Events.Single(e => e.Action == "tenant.update");
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);

        var stored = await registry.GetAsync(tenant.Id, ct);
        Assert.Equal(updated, stored); // the commit truthfully matches what the (durably real) event claims
    }

    [Fact]
    public async Task A_total_audit_store_outage_where_even_verification_fails_still_fails_closed()
    {
        // The narrow, honestly-disclosed remaining boundary: if BOTH the write and the
        // verify-on-throw check fail, there is genuinely no way to resolve the ambiguity
        // in-process, and the operation correctly refuses rather than guessing.
        var ct = TestContext.Current.CancellationToken;
        var service = new TenantService(new InMemoryTenantRegistry(), new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<TenantRegistryError>(
            () => service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
    }

    [Fact]
    public async Task A_cancellation_arriving_right_after_the_audit_write_succeeds_does_not_abort_the_commit()
    {
        // Kemi's heads-up (2026-09-22, cross-referencing Seyi's finding in Emeka's
        // BIO-DEV-001 / QA-03): the registry commit must not be abortable by the caller's
        // cancellation token once the audit write is durably confirmed, or a genuine
        // Allowed event can outlive a change that never reached the store. The in-memory
        // registry never actually checks its token today, so this exact bug cannot yet
        // manifest here - but it would reappear silently if the registry ever gained
        // cancellation support (e.g. a real DB-backed store with a cancellable driver
        // call). This test proves the commit is unconditional on the token, not merely
        // untested, so the guarantee survives that future change.
        var ct = TestContext.Current.CancellationToken;
        using var cts = new CancellationTokenSource();
        var registry = new ThrowsOnCancelledTokenRegistry(new InMemoryTenantRegistry());
        var sink = new CancelsCallerTokenAfterWriteAuditSink(new InMemoryTenantAuditSink(), cts);
        var service = new TenantService(registry, sink);

        // If the commit path still honoured the caller's token, cts being cancelled
        // between the audit write and the registry call would surface as
        // OperationCanceledException here instead of a successful result.
        var tenant = await service.CreateAsync(ValidCreateRequest(), Actor, CanonicalId.NewEventId(), cts.Token);

        Assert.True(cts.IsCancellationRequested); // the signal genuinely arrived...
        var stored = await registry.GetAsync(tenant.Id, ct);
        Assert.NotNull(stored); // ...and the commit went through anyway
    }

    private static async Task<(Tenant? Result, TenantRegistryError? Error)> TryRunAsync(Func<Task<Tenant>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (TenantRegistryError ex)
        {
            return (null, ex);
        }
    }

    /// <summary>A total, unrecoverable outage: both the write and the verify-on-throw check fail, so <c>AuditAsync</c> cannot resolve the ambiguity and must fail closed.</summary>
    private sealed class ThrowingAuditSink : ITenantAuditSink
    {
        public Task WriteAsync(TenantAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage: cannot even verify");
    }

    /// <summary>Delays only its first call, so the test's second concurrent caller gets a genuine chance to start while the first still holds the per-tenant lock.</summary>
    private sealed class DelayFirstCallAuditSink(ITenantAuditSink inner) : ITenantAuditSink
    {
        private int _calls;

        public async Task WriteAsync(TenantAuditEvent auditEvent, CancellationToken cancellationToken = default)
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

    /// <summary>Writes durably (so the event genuinely exists) but always throws on the write call itself, simulating a store that commits and then fails to acknowledge.</summary>
    private sealed class DurablyWritesThenThrowsAuditSink(ITenantAuditSink inner) : ITenantAuditSink
    {
        public async Task WriteAsync(TenantAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            throw new InvalidOperationException("synthetic ack failure after a successful durable write");
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    /// <summary>Cancels a shared <see cref="CancellationTokenSource"/> right after a successful write, simulating a cancellation signal arriving the instant the audit event is durably confirmed.</summary>
    private sealed class CancelsCallerTokenAfterWriteAuditSink(ITenantAuditSink inner, CancellationTokenSource cts) : ITenantAuditSink
    {
        public async Task WriteAsync(TenantAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            cts.Cancel();
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    /// <summary>Throws if the token it is actually called with is cancelled, so a test can prove a commit call was (or wasn't) made with the caller's live, cancellable token.</summary>
    private sealed class ThrowsOnCancelledTokenRegistry(ITenantRegistry inner) : ITenantRegistry
    {
        public Task<Tenant> CreateAsync(Tenant tenant, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.CreateAsync(tenant, cancellationToken);
        }

        public Task<Tenant?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            inner.GetAsync(id, cancellationToken);

        public Task<(IReadOnlyList<Tenant> Items, bool HasMore)> ListAsync(
            int limit, string? afterId, TenantStatus? statusFilter, CancellationToken cancellationToken = default) =>
            inner.ListAsync(limit, afterId, statusFilter, cancellationToken);

        public Task<Tenant> ReplaceAsync(Tenant expected, Tenant updated, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.ReplaceAsync(expected, updated, cancellationToken);
        }
    }
}
