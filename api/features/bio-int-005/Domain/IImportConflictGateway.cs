namespace Biolog.Features.ImportConflictReview.Domain;

/// <summary>
/// This feature's own narrow, READ-ONLY seam onto BIO-INT-002's already-computed field
/// comparisons - implemented in <c>Biolog.Api.Modules.BioInt005Module</c> by wrapping that
/// story's own already-registered, Scoped <c>ImportBatchService.ListBatchesAsync</c> directly,
/// the same cross-story gateway pattern every sibling story uses. No ProjectReference to
/// BIO-INT-002's project - see this project's own <c>.csproj</c> comment.
///
/// Deliberately exposes only a read of EVERY applied batch's row outcomes for one mapping -
/// never a write, never a way to trigger an import, never the mapping's own configuration.
/// BIO-INT-002's own <c>PreviewAsync</c> never persists anything (confirmed in that class's own
/// header), so only <c>ApplyAsync</c>'s durable, committed batches are ever visible here - a
/// dry-run preview is exploratory and intentionally never reaches this story's review queue at
/// all.
/// </summary>
public interface IImportConflictGateway
{
    Task<IReadOnlyList<ImportRowSnapshot>> ListRowOutcomesAsync(string mappingId, CancellationToken cancellationToken = default);
}
