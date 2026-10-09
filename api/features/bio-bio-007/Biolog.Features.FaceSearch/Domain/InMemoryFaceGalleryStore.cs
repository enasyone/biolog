using System.Collections.Concurrent;
using Biolog.Features.Biometrics.Domain;

namespace Biolog.Features.FaceSearch.Domain;

/// <summary>In-process, in-memory <see cref="IFaceGalleryStore"/> for local development and this
/// story's own tests - explicitly NOT a real persistence layer (mirrors BIO-BIO-001's
/// <c>InMemoryBiometricTemplateStore</c> scope note). Keyed by (tenantId, personId) so a lookup
/// for one tenant can never return another's templates (AC05) - the same
/// <see cref="ValueTuple{T1,T2}"/> key shape BIO-BIO-001-F02 established (never a delimiter-
/// joined string).</summary>
internal sealed class InMemoryFaceGalleryStore : IFaceGalleryStore
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId), BiometricTemplate> _gallery = new();

    public Task IndexAsync(BiometricTemplate template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        _gallery[(template.TenantId, template.PersonId)] = template;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BiometricTemplate>> GetAllForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        IReadOnlyList<BiometricTemplate> matches = _gallery
            .Where(kvp => kvp.Key.TenantId == tenantId)
            .Select(kvp => kvp.Value)
            .ToList();
        return Task.FromResult(matches);
    }

    public Task<bool> RemoveAsync(string tenantId, string personId, CancellationToken cancellationToken) =>
        Task.FromResult(_gallery.TryRemove((tenantId, personId), out _));
}
