using System.Collections.Concurrent;

namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>In-memory placeholder for <see cref="IBoothCaptureRepository"/>, pending real MySQL-backed persistence (ADR-0005) - same rationale as every sibling story's own in-memory repository.</summary>
internal sealed class InMemoryBoothCaptureRepository : IBoothCaptureRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId), ConcurrentDictionary<int, BoothCapture>> _byPerson = new();

    public Task<BoothCapture?> GetAsync(string tenantId, string personId, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        if (_byPerson.TryGetValue((tenantId, personId), out var captures) && captures.TryGetValue(sequenceNumber, out var capture))
        {
            return Task.FromResult<BoothCapture?>(capture);
        }

        return Task.FromResult<BoothCapture?>(null);
    }

    public Task<IReadOnlyList<BoothCapture>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        if (!_byPerson.TryGetValue((tenantId, personId), out var captures))
        {
            return Task.FromResult<IReadOnlyList<BoothCapture>>([]);
        }

        return Task.FromResult<IReadOnlyList<BoothCapture>>(captures.Values.OrderBy(c => c.SequenceNumber).ToList());
    }

    public Task<BoothCapture> CreateAsync(BoothCapture capture, CancellationToken cancellationToken = default)
    {
        var captures = _byPerson.GetOrAdd((capture.TenantId, capture.PersonId), static _ => new ConcurrentDictionary<int, BoothCapture>());
        if (!captures.TryAdd(capture.SequenceNumber, capture))
        {
            throw new PhotoBoothError(
                $"a capture already exists at this sequence: {capture.PersonId}/{capture.SequenceNumber}", "capture_sequence_conflict", 500);
        }

        return Task.FromResult(capture);
    }

    public Task<BoothCapture> ReplaceAsync(BoothCapture expectedCurrent, BoothCapture updated, CancellationToken cancellationToken = default)
    {
        if (expectedCurrent.Status != CaptureStatus.Pending)
        {
            throw PhotoBoothError.CaptureNotPending(expectedCurrent.PersonId, expectedCurrent.SequenceNumber, expectedCurrent.Status);
        }

        var key = (expectedCurrent.TenantId, expectedCurrent.PersonId);
        if (!_byPerson.TryGetValue(key, out var captures) ||
            !captures.TryGetValue(expectedCurrent.SequenceNumber, out var stored) ||
            stored.Version != expectedCurrent.Version)
        {
            var actual = _byPerson.TryGetValue(key, out var current) && current.TryGetValue(expectedCurrent.SequenceNumber, out var currentCapture)
                ? currentCapture.Version
                : 0;
            throw PhotoBoothError.CaptureConcurrencyConflict(expectedCurrent.PersonId, expectedCurrent.SequenceNumber, expectedCurrent.Version, actual);
        }

        captures[expectedCurrent.SequenceNumber] = updated;
        return Task.FromResult(updated);
    }
}
