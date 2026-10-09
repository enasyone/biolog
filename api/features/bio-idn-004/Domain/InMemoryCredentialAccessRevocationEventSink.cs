using System.Collections.Concurrent;

namespace Biolog.Features.PersonLifecycle.Domain;

/// <summary>In-process, append-only store for BIO-IDN-004's credential/access revocation events. Process-lifetime only, not durable across a restart. See <see cref="ICredentialAccessRevocationEventSink"/>.</summary>
public sealed class InMemoryCredentialAccessRevocationEventSink : ICredentialAccessRevocationEventSink
{
    private readonly ConcurrentQueue<CredentialAccessRevocationEvent> _events = new();

    public Task PublishAsync(CredentialAccessRevocationEvent revocationEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(revocationEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    public Task<bool> ExistsForSuspensionAsync(string tenantId, string personId, int suspendedAtVersion, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.TenantId == tenantId && e.PersonId == personId && e.SuspendedAtVersion == suspendedAtVersion));

    /// <summary>Read-only snapshot, oldest first. Exposed for tests and for a future revocation-consumer endpoint.</summary>
    public IReadOnlyList<CredentialAccessRevocationEvent> Events => [.. _events];
}
