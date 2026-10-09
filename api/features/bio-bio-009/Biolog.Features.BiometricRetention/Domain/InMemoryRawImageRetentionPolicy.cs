using System.Collections.Concurrent;

namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>BIO-BIO-009-AC03. A tenant absent from the map has never been authorised - the
/// default is off, by construction, not by a stored "false" value a bug could flip.</summary>
internal sealed class InMemoryRawImageRetentionPolicy : IRawImageRetentionPolicy
{
    private readonly ConcurrentDictionary<string, bool> _authorised = new(StringComparer.Ordinal);

    public Task<bool> IsAuthorisedAsync(string tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(_authorised.TryGetValue(tenantId, out var authorised) && authorised);

    public Task AuthoriseAsync(string tenantId, CancellationToken cancellationToken)
    {
        _authorised[tenantId] = true;
        return Task.CompletedTask;
    }

    public Task RevokeAsync(string tenantId, CancellationToken cancellationToken)
    {
        _authorised[tenantId] = false;
        return Task.CompletedTask;
    }
}
