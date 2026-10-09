namespace Biolog.Features.Notifications;

/// <summary>
/// BIO-RPT-001-AC01: "Rules subscribe to versioned domain events." This is the subscription
/// mechanism itself - a minimal in-process publish/subscribe bus, since no other story in
/// this codebase yet publishes a real domain event matching
/// `contracts/schemas/event-envelope.schema.json` (confirmed by searching the codebase
/// before building this - see docs/features/bio-rpt-001/README.md). A future story that
/// publishes real events depends on <see cref="IDomainEventBus"/> the same way this one's
/// own <see cref="NotificationDispatchService"/> subscribes to it, with no change needed
/// here.
///
/// <see cref="PublishAsync"/> awaits every subscribed handler in registration order before
/// returning, and isolates one handler's failure from the others (a thrown exception is
/// caught and does not stop the remaining handlers, and does not propagate to the
/// publisher) - the same "a producer's fan-out cannot be broken by one bad consumer" shape
/// a real message bus provides, kept here rather than deferred to a "later, real message
/// broker story" the way SIMULATION-mode adapters elsewhere in this codebase defer a real
/// vendor integration.
/// </summary>
public interface IDomainEventBus
{
    IDisposable Subscribe(Func<DomainEventEnvelope, CancellationToken, Task> handler);

    Task PublishAsync(DomainEventEnvelope envelope, CancellationToken cancellationToken);
}

public sealed class InMemoryDomainEventBus : IDomainEventBus
{
    private readonly List<Func<DomainEventEnvelope, CancellationToken, Task>> _handlers = [];
    private readonly Lock _gate = new();

    public IDisposable Subscribe(Func<DomainEventEnvelope, CancellationToken, Task> handler)
    {
        lock (_gate)
        {
            _handlers.Add(handler);
        }

        return new Subscription(this, handler);
    }

    public async Task PublishAsync(DomainEventEnvelope envelope, CancellationToken cancellationToken)
    {
        Func<DomainEventEnvelope, CancellationToken, Task>[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _handlers];
        }

        foreach (var handler in snapshot)
        {
            try
            {
                await handler(envelope, cancellationToken);
            }
            catch (Exception)
            {
                // One subscriber's failure must not prevent another subscriber from seeing
                // the same event, and must not fail the publisher - the publisher already
                // did its job the moment the event was accepted for fan-out.
            }
        }
    }

    private sealed class Subscription(InMemoryDomainEventBus bus, Func<DomainEventEnvelope, CancellationToken, Task> handler) : IDisposable
    {
        public void Dispose()
        {
            lock (bus._gate)
            {
                bus._handlers.Remove(handler);
            }
        }
    }
}
