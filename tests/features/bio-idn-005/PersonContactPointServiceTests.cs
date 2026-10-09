using Biolog.Features.DuplicateReview.Api;
using Biolog.Features.DuplicateReview.Audit;
using Biolog.Features.DuplicateReview.Domain;
using Biolog.Features.DuplicateReview.Identifiers;
using Xunit;

namespace Biolog.Features.DuplicateReview.Tests;

/// <summary>BIO-IDN-005-QA-01's fix: PersonContactPointService, a genuinely real, tenant-scoped contact-data source (see Domain.PersonContactPoint's header).</summary>
public sealed class PersonContactPointServiceTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonA = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    private static PersonContactPointService NewService(
        IReadOnlyList<PersonAttributes>? persons = null,
        IPersonContactPointRepository? repository = null,
        IDuplicateCandidateAuditSink? auditSink = null) =>
        new(
            new FakeCurrentTenantProvider(TenantId),
            repository ?? new InMemoryPersonContactPointRepository(),
            auditSink ?? new InMemoryDuplicateCandidateAuditSink(),
            new PersonContactPointLockRegistry(),
            new FakePersonAttributeGateway(persons ?? [new PersonAttributes(PersonA, "Amara", "Eze", "2004-01-01")]));

    [Fact]
    public async Task Set_creates_a_new_contact_point_at_version_1()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var created = await service.SetAsync(PersonA, new SetContactPointRequest { Email = "amara.eze@example.edu.ng" }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal("amara.eze@example.edu.ng", created.Email);
        Assert.Equal(1, created.Version);
    }

    [Fact]
    public async Task Set_normalizes_email_to_trimmed_lowercase()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var created = await service.SetAsync(PersonA, new SetContactPointRequest { Email = "  Amara.Eze@Example.edu.NG  " }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal("amara.eze@example.edu.ng", created.Email);
    }

    [Fact]
    public async Task Set_rejects_a_malformed_email()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(
            () => service.SetAsync(PersonA, new SetContactPointRequest { Email = "not-an-email" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Set_for_a_person_that_does_not_exist_in_the_resolved_tenant_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(persons: []); // nobody exists

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(
            () => service.SetAsync(PersonA, new SetContactPointRequest { Email = "amara.eze@example.edu.ng" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("person_not_found", error.Code);
    }

    [Fact]
    public async Task The_existence_check_fails_closed_no_audit_event_and_nothing_is_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryDuplicateCandidateAuditSink();
        var repository = new InMemoryPersonContactPointRepository();
        var service = NewService(persons: [], repository: repository, auditSink: sink);

        await Assert.ThrowsAsync<DuplicateCandidateError>(
            () => service.SetAsync(PersonA, new SetContactPointRequest { Email = "amara.eze@example.edu.ng" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Empty(sink.Events);
        Assert.Null(await repository.GetAsync(TenantId, PersonA, ct));
    }

    [Fact]
    public async Task Set_a_second_time_requires_the_current_version_and_updates_in_place()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var created = await service.SetAsync(PersonA, new SetContactPointRequest { Email = "amara.eze@example.edu.ng" }, Actor, CanonicalId.NewEventId(), ct);

        var updated = await service.SetAsync(
            PersonA, new SetContactPointRequest { Email = "amara.new@example.edu.ng", Version = created.Version }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(2, updated.Version);
        Assert.Equal("amara.new@example.edu.ng", updated.Email);
    }

    [Fact]
    public async Task Set_a_second_time_without_a_version_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        await service.SetAsync(PersonA, new SetContactPointRequest { Email = "amara.eze@example.edu.ng" }, Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(
            () => service.SetAsync(PersonA, new SetContactPointRequest { Email = "amara.new@example.edu.ng" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Set_a_second_time_with_a_stale_version_is_a_concurrency_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();
        var created = await service.SetAsync(PersonA, new SetContactPointRequest { Email = "amara.eze@example.edu.ng" }, Actor, CanonicalId.NewEventId(), ct);
        await service.SetAsync(PersonA, new SetContactPointRequest { Email = "amara.two@example.edu.ng", Version = created.Version }, Actor, CanonicalId.NewEventId(), ct);

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => service.SetAsync(
            PersonA, new SetContactPointRequest { Email = "amara.three@example.edu.ng", Version = created.Version }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task Get_returns_null_when_nothing_is_on_file()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService();

        Assert.Null(await service.GetAsync(PersonA, ct));
    }

    [Fact]
    public async Task Audit_trail_withholds_the_actual_email_and_phone_values()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryDuplicateCandidateAuditSink();
        var service = NewService(auditSink: sink);

        await service.SetAsync(PersonA, new SetContactPointRequest { Email = "amara.eze@example.edu.ng", Phone = "08012345678" }, Actor, CanonicalId.NewEventId(), ct);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("person_contact_point.set", evt.Action);
        Assert.Contains("email", evt.Withheld);
        Assert.Contains("phone", evt.Withheld);
        Assert.DoesNotContain(evt.Detail.Values, v => Equals(v, "amara.eze@example.edu.ng") || Equals(v, "08012345678"));
        Assert.Equal("internal", evt.Classification); // never escalates to a PII-bearing classification (see AuditEventBuilder's header)
    }

    [Fact]
    public async Task Set_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonContactPointRepository();
        var service = NewService(repository: repository, auditSink: new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(
            () => service.SetAsync(PersonA, new SetContactPointRequest { Email = "amara.eze@example.edu.ng" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Null(await repository.GetAsync(TenantId, PersonA, ct));
    }

    private sealed class ThrowingAuditSink : IDuplicateCandidateAuditSink
    {
        public Task WriteAsync(DuplicateCandidateAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage: cannot even verify");
    }
}
