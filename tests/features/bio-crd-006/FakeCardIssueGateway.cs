using Biolog.Features.CardLifecycle.Audit;
using Biolog.Features.CardLifecycle.Domain;

namespace Biolog.Features.CardLifecycle.Tests;

/// <summary>
/// Test double for <see cref="ICardIssueGateway"/> - simulates BIO-CRD-005's own real behavior
/// (sequence assignment, activation's "at most one active per (person, card class)" invariant,
/// concurrency-conflict/precondition-required errors) closely enough for this feature's own
/// unit/integration tests, which never construct a real
/// <c>Biolog.Features.CardIssuance.CardIssueService</c> (this project has no
/// <c>ProjectReference</c> to that one - see this project's csproj remarks). The REAL
/// cross-story dependency is exercised only at the packaged host level - see
/// <c>tests/dotnet/Biolog.IntegrationTests/CardLifecycleHostChannelTests.cs</c> - mirroring
/// BIO-CRD-005's own identical precedent for its dependency on BIO-IDN-001.
/// </summary>
public sealed class FakeCardIssueGateway : ICardIssueGateway
{
    private readonly object _lock = new();
    private readonly Dictionary<(string PersonId, string CardClass, int SequenceNumber), CardIssueSnapshot> _byKey = new();

    /// <summary>Simulates BIO-CRD-005's own real Idempotency-Key replay on activation closely enough for tests that assert a retried activate-replacement does not re-execute.</summary>
    private readonly Dictionary<string, CardIssueSnapshot> _activateIdempotency = new(StringComparer.Ordinal);

    public Task<CardIssueSnapshot?> GetAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_byKey.TryGetValue((personId, cardClass, sequenceNumber), out var s) ? s : null);
        }
    }

    public Task<IReadOnlyList<CardIssueSnapshot>> ListAsync(string personId, string cardClass, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlyList<CardIssueSnapshot> list = [.. _byKey.Values
                .Where(s => s.PersonId == personId && s.CardClass == cardClass)
                .OrderBy(s => s.SequenceNumber)];
            return Task.FromResult(list);
        }
    }

    public Task<CardIssueSnapshot> IssueReplacementAsync(string personId, string cardClass, ReplacementCardRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var next = _byKey.Values.Where(s => s.PersonId == personId && s.CardClass == cardClass).Select(s => s.SequenceNumber).DefaultIfEmpty(0).Max() + 1;
            var snapshot = new CardIssueSnapshot { PersonId = personId, CardClass = cardClass, SequenceNumber = next, Status = CardIssueLifecycleStatus.Issued, Version = 1 };
            _byKey[(personId, cardClass, next)] = snapshot;
            return Task.FromResult(snapshot);
        }
    }

    public Task<CardIssueSnapshot> ActivateAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, string idempotencyKey, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var replayKey = $"{personId}\u0000{cardClass}\u0000{sequenceNumber}\u0000{idempotencyKey}";
            if (_activateIdempotency.TryGetValue(replayKey, out var cached))
            {
                return Task.FromResult(cached);
            }

            if (!_byKey.TryGetValue((personId, cardClass, sequenceNumber), out var current))
            {
                throw CardLifecycleError.CardIssueNotFound(personId, cardClass, sequenceNumber);
            }

            if (expectedVersion is not int expected)
            {
                throw CardLifecycleError.PreconditionRequired();
            }

            if (expected != current.Version)
            {
                throw CardLifecycleError.ConcurrencyConflict(expected, current.Version);
            }

            if (current.Status != CardIssueLifecycleStatus.Issued)
            {
                throw new CardLifecycleError($"cannot activate a card issue currently {current.Status}", "invalid_card_issue_transition", 409);
            }

            var active = _byKey.Values.FirstOrDefault(s => s.PersonId == personId && s.CardClass == cardClass && s.Status == CardIssueLifecycleStatus.Active);
            if (active is not null)
            {
                throw new CardLifecycleError($"another card already active at sequence {active.SequenceNumber}", "another_card_already_active", 409);
            }

            var updated = current with { Status = CardIssueLifecycleStatus.Active, Version = current.Version + 1 };
            _byKey[(personId, cardClass, sequenceNumber)] = updated;
            _activateIdempotency[replayKey] = updated;
            return Task.FromResult(updated);
        }
    }

    public Task<CardIssueSnapshot> DeactivateAsync(string personId, string cardClass, int sequenceNumber, int expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_byKey.TryGetValue((personId, cardClass, sequenceNumber), out var current))
            {
                throw CardLifecycleError.CardIssueNotFound(personId, cardClass, sequenceNumber);
            }

            if (expectedVersion != current.Version)
            {
                throw CardLifecycleError.ConcurrencyConflict(expectedVersion, current.Version);
            }

            var updated = current with { Status = CardIssueLifecycleStatus.Deactivated, Version = current.Version + 1 };
            _byKey[(personId, cardClass, sequenceNumber)] = updated;
            return Task.FromResult(updated);
        }
    }

    /// <summary>Test setup helper: seeds an existing card issue directly, representing a card issued by BIO-CRD-005 before this feature's own report/recover/replace flow begins.</summary>
    public CardIssueSnapshot Seed(string personId, string cardClass, int sequenceNumber, CardIssueLifecycleStatus status = CardIssueLifecycleStatus.Active, int version = 1)
    {
        lock (_lock)
        {
            var snapshot = new CardIssueSnapshot { PersonId = personId, CardClass = cardClass, SequenceNumber = sequenceNumber, Status = status, Version = version };
            _byKey[(personId, cardClass, sequenceNumber)] = snapshot;
            return snapshot;
        }
    }
}
