using Biolog.Features.ImportConflictReview.Domain;

namespace Biolog.Features.ImportConflictReview.Tests;

/// <summary>A per-test, directly-settable stand-in for BIO-INT-002's own already-computed field comparisons - mirrors the "feature test DI solution" convention every sibling feature test project already uses (inline fakes, never a real cross-project dependency).</summary>
public sealed class FakeImportConflictGateway(IReadOnlyList<ImportRowSnapshot> rowOutcomes) : IImportConflictGateway
{
    public Task<IReadOnlyList<ImportRowSnapshot>> ListRowOutcomesAsync(string mappingId, CancellationToken cancellationToken = default) =>
        Task.FromResult(rowOutcomes);
}
