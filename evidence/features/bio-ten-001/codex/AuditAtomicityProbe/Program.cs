using Biolog.Features.TenantRegistry;
using Biolog.Features.TenantRegistry.Api;
using Biolog.Features.TenantRegistry.Audit;
using Biolog.Features.TenantRegistry.Domain;
using Biolog.Features.TenantRegistry.Identifiers;

var registry = new InMemoryTenantRegistry();
var seedSink = new InMemoryTenantAuditSink();
var seed = await new TenantService(registry, seedSink).CreateAsync(
    new CreateTenantRequest { Name = "Seed", LicensePlan = "basic", LicenseSeats = 1, RoutingRegion = "test-region" },
    new AuditActor(ActorType.User, "seed"), CanonicalId.NewEventId());

// Production registers one singleton TenantService. Force writer A to yield after its audit
// enters the sink, start writer B against the same observed version, and verify B cannot
// reach audit while A owns the tenant lock.
var sink = new FirstWriteBlockingSink();
var service = new TenantService(registry, sink);
var taskA = Capture(() => service.UpdateAsync(seed.Id,
    new UpdateTenantRequest { Version = seed.Version, Name = "Writer A" },
    new AuditActor(ActorType.User, "writer-a"), CanonicalId.NewEventId()));
await sink.FirstWriteEntered;

var taskB = Capture(() => service.UpdateAsync(seed.Id,
    new UpdateTenantRequest { Version = seed.Version, LicenseSeats = 2 },
    new AuditActor(ActorType.User, "writer-b"), CanonicalId.NewEventId()));

await Task.Delay(50);
if (taskB.IsCompleted || sink.Events.Count != 1)
{
    Console.Error.WriteLine("FAIL second writer crossed the tenant lock before the first commit");
    return 1;
}

sink.Release();
var outcomes = await Task.WhenAll(taskA, taskB);
if (outcomes.Count(x => x.Result is not null) != 1 ||
    outcomes.Count(x => x.Error?.Code == "concurrency_conflict") != 1 ||
    sink.Events.Count != 1)
{
    Console.Error.WriteLine("FAIL concurrent stale writer produced an invalid result or audit event");
    return 1;
}

var stored = await registry.GetAsync(seed.Id);
var winner = outcomes.Single(x => x.Result is not null).Result!;
if (stored != winner || sink.Events.Single().Outcome != AuditOutcome.Allowed)
{
    Console.Error.WriteLine("FAIL committed state does not match the sole ALLOWED audit");
    return 1;
}

Console.WriteLine("PASS singleton service serializes same-tenant writers; rejected writer has no ALLOWED audit");
return 0;

static async Task<(Tenant? Result, TenantRegistryError? Error)> Capture(Func<Task<Tenant>> action)
{
    try { return (await action(), null); }
    catch (TenantRegistryError error) { return (null, error); }
}

file sealed class FirstWriteBlockingSink : ITenantAuditSink
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<TenantAuditEvent> Events { get; } = [];
    public Task FirstWriteEntered => _entered.Task;
    public void Release() => _release.TrySetResult();

    public async Task WriteAsync(TenantAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(auditEvent);
        if (Events.Count == 1)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Events.Any(e => e.EventId == eventId));
}
