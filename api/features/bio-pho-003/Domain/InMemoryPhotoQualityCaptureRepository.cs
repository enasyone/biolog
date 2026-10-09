using System.Collections.Concurrent;

namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>In-memory placeholder for <see cref="IPhotoQualityCaptureRepository"/>, pending real MySQL-backed persistence (ADR-0005) - same rationale as every sibling story's own in-memory repository.</summary>
internal sealed class InMemoryPhotoQualityCaptureRepository : IPhotoQualityCaptureRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId), ConcurrentDictionary<int, PhotoQualityCapture>> _byPerson = new();

    public Task<PhotoQualityCapture?> GetAsync(string tenantId, string personId, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        if (_byPerson.TryGetValue((tenantId, personId), out var captures) && captures.TryGetValue(sequenceNumber, out var capture))
        {
            return Task.FromResult<PhotoQualityCapture?>(capture);
        }

        return Task.FromResult<PhotoQualityCapture?>(null);
    }

    public Task<IReadOnlyList<PhotoQualityCapture>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        if (!_byPerson.TryGetValue((tenantId, personId), out var captures))
        {
            return Task.FromResult<IReadOnlyList<PhotoQualityCapture>>([]);
        }

        return Task.FromResult<IReadOnlyList<PhotoQualityCapture>>(captures.Values.OrderBy(c => c.SequenceNumber).ToList());
    }

    public Task<PhotoQualityCapture> CreateAsync(PhotoQualityCapture capture, CancellationToken cancellationToken = default)
    {
        var captures = _byPerson.GetOrAdd((capture.TenantId, capture.PersonId), static _ => new ConcurrentDictionary<int, PhotoQualityCapture>());
        if (!captures.TryAdd(capture.SequenceNumber, capture))
        {
            throw new PhotoQualityError(
                $"a capture already exists at this sequence: {capture.PersonId}/{capture.SequenceNumber}", "capture_sequence_conflict", 500);
        }

        return Task.FromResult(capture);
    }

    public Task<PhotoQualityCapture> ReplaceAsync(PhotoQualityCapture expectedCurrent, PhotoQualityCapture updated, CancellationToken cancellationToken = default)
    {
        if (expectedCurrent.Status != PhotoQualityCaptureStatus.Pending)
        {
            throw PhotoQualityError.CaptureNotPending(expectedCurrent.PersonId, expectedCurrent.SequenceNumber, expectedCurrent.Status);
        }

        var key = (expectedCurrent.TenantId, expectedCurrent.PersonId);
        if (!_byPerson.TryGetValue(key, out var captures) ||
            !captures.TryGetValue(expectedCurrent.SequenceNumber, out var stored) ||
            stored.Version != expectedCurrent.Version)
        {
            var actual = _byPerson.TryGetValue(key, out var current) && current.TryGetValue(expectedCurrent.SequenceNumber, out var currentCapture)
                ? currentCapture.Version
                : 0;
            throw PhotoQualityError.CaptureConcurrencyConflict(expectedCurrent.PersonId, expectedCurrent.SequenceNumber, expectedCurrent.Version, actual);
        }

        captures[expectedCurrent.SequenceNumber] = updated;
        return Task.FromResult(updated);
    }
}
