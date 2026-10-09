namespace Biolog.Features.Integrations.Domain;

/// <summary>BIO-INT-002-AC03/AC04: what happened to one input row, decided by comparing the incoming row against any already-linked PERSON.</summary>
public enum RowOutcome
{
    /// <summary>No existing link for this external key - a brand-new PERSON would be (or was) created.</summary>
    Created,

    /// <summary>Already linked, and every incoming field either matches the current PERSON value or the current value is simply unset - nothing to apply, nothing to reject.</summary>
    Unchanged,

    /// <summary>
    /// Already linked, and at least one incoming field value DISAGREES with the PERSON's current
    /// value for that field. BIO-INT-002-AC04's central guarantee: this row is NEVER applied -
    /// see <see cref="ImportBatchService"/>'s own header for why conflict RESOLUTION (actually
    /// correcting the field) is explicitly out of this story's scope.
    /// </summary>
    Conflict,

    /// <summary>The mapping this row targets is configured <see cref="SyncDirection.BiologIsMaster"/> - rejected outright, never evaluated further (BIO-INT-002-AC02).</summary>
    Rejected,
}

public static class RowOutcomeExtensions
{
    public static string ToWireString(this RowOutcome value) => value switch
    {
        RowOutcome.Created => "created",
        RowOutcome.Unchanged => "unchanged",
        RowOutcome.Conflict => "conflict",
        RowOutcome.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unreachable: exhaustive switch"),
    };
}

/// <summary>One field-level disagreement between the incoming row and the currently-linked PERSON's own value - never applied (AC04).</summary>
public sealed record FieldConflict(string PersonField, string? ExistingValue, string? IncomingValue);

/// <summary>The outcome of processing one input row, whether in <see cref="ImportBatchMode.DryRun"/> or <see cref="ImportBatchMode.Applied"/> mode.</summary>
public sealed record ImportRowOutcome
{
    public required string ExternalKey { get; init; }
    public required RowOutcome Outcome { get; init; }
    public string? PersonId { get; init; }
    public IReadOnlyList<FieldConflict> Conflicts { get; init; } = [];
}

public enum ImportBatchMode
{
    DryRun,
    Applied,
}

public static class ImportBatchModeExtensions
{
    public static string ToWireString(this ImportBatchMode value) => value switch
    {
        ImportBatchMode.DryRun => "dry_run",
        ImportBatchMode.Applied => "applied",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// BIO-INT-002-AC03/AC05: one preview-or-apply run against one <see cref="ImportSourceMapping"/>.
/// Write-once - nothing ever mutates a batch after it is recorded, so no CAS/version is needed
/// (unlike <see cref="ImportSourceMapping"/>, which IS mutated in place by config edits).
/// </summary>
public sealed record ImportBatch
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string MappingId { get; init; }
    public required ImportBatchMode Mode { get; init; }
    public required string SubmittedAt { get; init; }
    public required IReadOnlyList<ImportRowOutcome> RowOutcomes { get; init; }
    public string? IdempotencyKey { get; init; }
}
