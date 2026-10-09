namespace Biolog.Features.ImportConflictReview.Domain;

/// <summary>This feature's own local copy of BIO-INT-002's <c>FieldConflict</c> - never that project's own type (cross-project types never cross a gateway interface boundary, same discipline every sibling gateway in this epic applies). Carries the EXISTING (current PERSON) value and the INCOMING (source) value for one disagreeing field - AC01's "conflict compares source versus BIOLOG values."</summary>
public sealed record ImportFieldConflict(string PersonField, string? ExistingValue, string? IncomingValue);

/// <summary>Mirrors BIO-INT-002's own <c>RowOutcome</c> wire vocabulary - this feature's own local copy, read through <see cref="IImportConflictGateway"/>.</summary>
public enum RowOutcomeSnapshot
{
    Created,
    Unchanged,
    Conflict,
    Rejected,
}

/// <summary>
/// One row's outcome as it stood in ONE applied import batch, as reported by
/// <see cref="IImportConflictGateway.ListRowOutcomesAsync"/>. <see cref="ImportConflictReviewService.ScanAsync"/>
/// reduces a mapping's full outcome history (across every applied batch) down to the MOST
/// RECENT snapshot per <see cref="ExternalKey"/> before deciding what to raise, augment or
/// withdraw - see that method's own header for why "most recent wins" is the correct reduction.
/// </summary>
public sealed record ImportRowSnapshot
{
    public required string ExternalKey { get; init; }
    public required RowOutcomeSnapshot Outcome { get; init; }
    public string? PersonId { get; init; }
    public IReadOnlyList<ImportFieldConflict> Conflicts { get; init; } = [];
    public required string BatchId { get; init; }
    public required string SubmittedAt { get; init; }
}
