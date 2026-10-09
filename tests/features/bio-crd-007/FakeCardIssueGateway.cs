using Biolog.Features.CardProduction.Audit;
using Biolog.Features.CardProduction.Domain;

namespace Biolog.Features.CardProduction.Tests;

/// <summary>
/// Test double for <see cref="ICardIssueGateway"/> - simulates BIO-CRD-005's own real
/// activation behaviour (existence check, expected_version precondition,
/// "at most one Active" invariant) closely enough for this feature's own unit/integration
/// tests, which never construct a real <c>Biolog.Features.CardIssuance.CardIssueService</c>
/// (this project has no <c>ProjectReference</c> to that one). The REAL cross-story dependency is
/// exercised only at the packaged host level - see
/// <c>tests/dotnet/Biolog.IntegrationTests/CardProductionHostChannelTests.cs</c>.
/// </summary>
public sealed class FakeCardIssueGateway : ICardIssueGateway
{
    private readonly object _lock = new();
    private readonly Dictionary<(string PersonId, string CardClass, int SequenceNumber), CardIssueSnapshot> _byKey = new();

    public Task<CardIssueSnapshot?> GetAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_byKey.TryGetValue((personId, cardClass, sequenceNumber), out var s) ? s : null);
        }
    }

    public Task<CardIssueSnapshot> ActivateAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, string idempotencyKey, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_byKey.TryGetValue((personId, cardClass, sequenceNumber), out var current))
            {
                throw ProductionError.CardIssueNotFound(personId, cardClass, sequenceNumber);
            }

            if (expectedVersion is not int expected)
            {
                throw ProductionError.PreconditionRequired();
            }

            if (expected != current.Version)
            {
                throw ProductionError.ConcurrencyConflict(expected, current.Version);
            }

            if (current.Status != CardIssueLifecycleStatus.Issued)
            {
                throw new ProductionError($"cannot activate a card issue currently {current.Status}", "invalid_card_issue_transition", 409);
            }

            var active = _byKey.Values.FirstOrDefault(s => s.PersonId == personId && s.CardClass == cardClass && s.Status == CardIssueLifecycleStatus.Active);
            if (active is not null)
            {
                throw new ProductionError($"another card already active at sequence {active.SequenceNumber}", "another_card_already_active", 409);
            }

            var updated = current with { Status = CardIssueLifecycleStatus.Active, Version = current.Version + 1 };
            _byKey[(personId, cardClass, sequenceNumber)] = updated;
            return Task.FromResult(updated);
        }
    }

    /// <summary>Test setup helper: seeds an existing card issue directly, representing a card issued by BIO-CRD-005.</summary>
    public CardIssueSnapshot Seed(string personId, string cardClass, int sequenceNumber, CardIssueLifecycleStatus status = CardIssueLifecycleStatus.Issued, int version = 1)
    {
        lock (_lock)
        {
            var snapshot = new CardIssueSnapshot { PersonId = personId, CardClass = cardClass, SequenceNumber = sequenceNumber, Status = status, Version = version };
            _byKey[(personId, cardClass, sequenceNumber)] = snapshot;
            return snapshot;
        }
    }
}
