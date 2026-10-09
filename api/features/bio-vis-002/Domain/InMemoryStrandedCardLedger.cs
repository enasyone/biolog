using System.Collections.Concurrent;
using Biolog.Features.VisitorCredentials.Identifiers;

namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>See <see cref="IStrandedCardLedger"/> for why this exists. A single, process-lifetime, cross-tenant dictionary, the same simplification every in-memory store in this codebase draws around itself.</summary>
internal sealed class InMemoryStrandedCardLedger : IStrandedCardLedger
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId, string CardClass, int SequenceNumber), bool> _stranded = new();

    /// <summary>F05's own remediation: entries present here are resolved (card confirmed no longer Active) - a separate set rather than removing from <see cref="_stranded"/> so the entry's own history is never lost.</summary>
    private readonly ConcurrentDictionary<(string TenantId, string PersonId, string CardClass, int SequenceNumber), bool> _resolved = new();

    /// <summary>F07's own remediation - see <see cref="IStrandedCardLedger.GetOrCreateReconciliationEventIdAsync"/>.</summary>
    private readonly ConcurrentDictionary<(string TenantId, string PersonId, string CardClass, int SequenceNumber), string> _reconciliationEventIds = new();

    public Task MarkStrandedAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stranded[(tenantId, personId, cardClass, sequenceNumber)] = true;
        return Task.CompletedTask;
    }

    public Task<bool> IsStrandedAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (tenantId, personId, cardClass, sequenceNumber);
        return Task.FromResult(_stranded.ContainsKey(key) && !_resolved.ContainsKey(key));
    }

    public Task<IReadOnlyList<int>> ListUnresolvedAsync(string tenantId, string personId, string cardClass, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<int> sequenceNumbers = [.. _stranded.Keys
            .Where(k => k.TenantId == tenantId && k.PersonId == personId && k.CardClass == cardClass && !_resolved.ContainsKey(k))
            .Select(k => k.SequenceNumber)];
        return Task.FromResult(sequenceNumbers);
    }

    public Task MarkResolvedAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _resolved[(tenantId, personId, cardClass, sequenceNumber)] = true;
        return Task.CompletedTask;
    }

    public Task<string> GetOrCreateReconciliationEventIdAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (tenantId, personId, cardClass, sequenceNumber);
        var eventId = _reconciliationEventIds.GetOrAdd(key, _ => CanonicalId.NewEventId());
        return Task.FromResult(eventId);
    }
}
