// Developer tests for BIO-CRD-006. SYNTHETIC fixtures only.
using Biolog.Features.CardLifecycle.Domain;

namespace Biolog.Features.CardLifecycle.Tests;

/// <summary>BIO-OPS-003-AC04's own fake seam. Unseeded cards default to <c>HasPassed = true</c>, so every test written before this precondition existed keeps exercising its own real scenario without needing to know about it.</summary>
public sealed class FakeProductionStatusGateway : IProductionStatusGateway
{
    private readonly Dictionary<(string PersonId, string CardClass, int SequenceNumber), ProductionStatusSnapshot> _byKey = new();

    public void Seed(string personId, string cardClass, int sequenceNumber, bool hasPassed, int? latestJobSequenceNumber = null, ProductionJobLifecycleStatus? latestStatus = null)
    {
        _byKey[(personId, cardClass, sequenceNumber)] = new ProductionStatusSnapshot
        {
            HasPassed = hasPassed,
            LatestJobSequenceNumber = latestJobSequenceNumber,
            LatestStatus = latestStatus,
        };
    }

    public Task<ProductionStatusSnapshot> HasPassedAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        if (_byKey.TryGetValue((personId, cardClass, sequenceNumber), out var snapshot))
        {
            return Task.FromResult(snapshot);
        }

        return Task.FromResult(new ProductionStatusSnapshot { HasPassed = true });
    }
}
