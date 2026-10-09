using Biolog.Features.Photography.Audit;
using Biolog.Features.Photography.Domain;
using Xunit;

namespace Biolog.Features.Photography.Tests;

public sealed class PhotoAssignmentServiceTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private static readonly AuditActor Actor = new(ActorType.User, "actor_1");

    private static (PhotoMasterService Masters, PhotoAssignmentService Assignments, InMemoryPhotoAuditSink AuditSink) MakeServices()
    {
        var masterRepo = new InMemoryPhotoMasterRepository();
        var assignmentRepo = new InMemoryPhotoAssignmentRepository();
        var tenantProvider = new FakeCurrentTenantProvider(TenantId);
        var auditSink = new InMemoryPhotoAuditSink();

        var masters = new PhotoMasterService(
            tenantProvider, masterRepo, new FakePersonExistenceGateway(), new InMemoryPhotoAuditSink(), new PhotoMasterLockRegistry());
        var assignments = new PhotoAssignmentService(
            tenantProvider, assignmentRepo, masterRepo, new FakePersonExistenceGateway(), auditSink, new PhotoAssignmentLockRegistry());

        return (masters, assignments, auditSink);
    }

    [Fact]
    public async Task AC04_the_first_assignment_for_a_person_starts_at_version_1_with_no_expected_version()
    {
        var (masters, assignments, _) = MakeServices();
        var ct = TestContext.Current.CancellationToken;
        var master = await masters.AppendAsync(PersonId, SyntheticImages.Png(10, 10), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", ct);

        var assignment = await assignments.AssignAsync(PersonId, master.Version, expectedVersion: null, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", ct);

        Assert.Equal(1, assignment.Version);
        Assert.Equal(master.Version, assignment.CurrentMasterVersion);
    }

    [Fact]
    public async Task AC04_reassigning_with_the_correct_expected_version_increments_the_version()
    {
        var (masters, assignments, _) = MakeServices();
        var ct = TestContext.Current.CancellationToken;
        var v1 = await masters.AppendAsync(PersonId, SyntheticImages.Png(10, 10), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", ct);
        var v2 = await masters.AppendAsync(PersonId, SyntheticImages.Jpeg(20, 20), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", ct);
        var first = await assignments.AssignAsync(PersonId, v1.Version, null, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000003", ct);

        var second = await assignments.AssignAsync(PersonId, v2.Version, first.Version, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000004", ct);

        Assert.Equal(2, second.Version);
        Assert.Equal(v2.Version, second.CurrentMasterVersion);
    }

    [Fact]
    public async Task Reassigning_with_a_stale_expected_version_is_rejected_with_concurrency_conflict()
    {
        var (masters, assignments, _) = MakeServices();
        var ct = TestContext.Current.CancellationToken;
        var v1 = await masters.AppendAsync(PersonId, SyntheticImages.Png(10, 10), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", ct);
        await assignments.AssignAsync(PersonId, v1.Version, null, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", ct);

        var error = await Assert.ThrowsAsync<PhotoError>(() =>
            assignments.AssignAsync(PersonId, v1.Version, expectedVersion: 999, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000003", ct));
        Assert.Equal("concurrency_conflict", error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public async Task Reassigning_an_existing_assignment_without_an_expected_version_fails_closed_with_precondition_required()
    {
        var (masters, assignments, _) = MakeServices();
        var ct = TestContext.Current.CancellationToken;
        var v1 = await masters.AppendAsync(PersonId, SyntheticImages.Png(10, 10), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", ct);
        await assignments.AssignAsync(PersonId, v1.Version, null, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", ct);

        var error = await Assert.ThrowsAsync<PhotoError>(() =>
            assignments.AssignAsync(PersonId, v1.Version, expectedVersion: null, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000003", ct));
        Assert.Equal("precondition_required", error.Code);
        Assert.Equal(428, error.Status);
    }

    [Fact]
    public async Task Creating_a_first_assignment_with_an_expected_version_is_rejected()
    {
        var (masters, assignments, _) = MakeServices();
        var ct = TestContext.Current.CancellationToken;
        var v1 = await masters.AppendAsync(PersonId, SyntheticImages.Png(10, 10), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", ct);

        var error = await Assert.ThrowsAsync<PhotoError>(() =>
            assignments.AssignAsync(PersonId, v1.Version, expectedVersion: 1, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", ct));
        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task Assigning_a_master_version_that_does_not_exist_for_this_person_is_rejected()
    {
        var (_, assignments, _) = MakeServices();

        var error = await Assert.ThrowsAsync<PhotoError>(() =>
            assignments.AssignAsync(PersonId, 999, null, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", TestContext.Current.CancellationToken));
        Assert.Equal("photo_master_not_found", error.Code);
    }

    [Fact]
    public async Task Concurrent_reassignments_racing_on_the_same_expected_version_leave_exactly_one_winner()
    {
        var (masters, assignments, _) = MakeServices();
        var ct = TestContext.Current.CancellationToken;
        var v1 = await masters.AppendAsync(PersonId, SyntheticImages.Png(10, 10), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", ct);
        var v2 = await masters.AppendAsync(PersonId, SyntheticImages.Jpeg(20, 20), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", ct);
        var v3 = await masters.AppendAsync(PersonId, SyntheticImages.Png(30, 30), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000003", ct);
        var first = await assignments.AssignAsync(PersonId, v1.Version, null, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000004", ct);

        var raceA = assignments.AssignAsync(PersonId, v2.Version, first.Version, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000005", ct);
        var raceB = assignments.AssignAsync(PersonId, v3.Version, first.Version, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000006", ct);

        var results = await Task.WhenAll(raceA.ContinueWith(t => t, ct), raceB.ContinueWith(t => t, ct));
        var succeeded = results.Count(t => !t.IsFaulted);
        var conflicted = results.Count(t => t.IsFaulted && t.Exception!.InnerException is PhotoError { Code: "concurrency_conflict" });

        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);
    }

    [Fact]
    public async Task AC04_every_reassignment_is_audited_with_the_previous_and_new_master_version()
    {
        var (masters, assignments, auditSink) = MakeServices();
        var ct = TestContext.Current.CancellationToken;
        var v1 = await masters.AppendAsync(PersonId, SyntheticImages.Png(10, 10), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000001", ct);
        var v2 = await masters.AppendAsync(PersonId, SyntheticImages.Jpeg(20, 20), Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000002", ct);
        var first = await assignments.AssignAsync(PersonId, v1.Version, null, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000003", ct);

        await assignments.AssignAsync(PersonId, v2.Version, first.Version, Actor, "evt_018f2e5a-aaaa-7bbb-8ccc-000000000004", ct);

        var assignmentEvents = auditSink.Events.Where(e => e.Target.Type == "photo_assignment").ToList();
        Assert.Equal(2, assignmentEvents.Count);
        Assert.Null(assignmentEvents[0].Detail["previous_master_version"]);
        Assert.Equal(v1.Version, assignmentEvents[1].Detail["previous_master_version"]);
        Assert.Equal(v2.Version, assignmentEvents[1].Detail["new_master_version"]);
    }
}
