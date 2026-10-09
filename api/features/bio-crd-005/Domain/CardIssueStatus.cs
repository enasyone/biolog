namespace Biolog.Features.CardIssuance.Domain;

/// <summary>
/// BIO-CRD-005-AC04's minimal lifecycle - deliberately separate from the encode/print/verify
/// timestamps (AC03), which record OPERATIONAL steps that may happen at any status. This enum
/// exists only to answer the one question AC04 cares about: is this specific physical card
/// currently the active one for its (person, card class)?
/// </summary>
public enum CardIssueStatus
{
    /// <summary>Sequence assigned, provenance recorded - not yet the active card.</summary>
    Issued,

    /// <summary>Exactly one card per (TenantId, PersonId, CardClass) may hold this status at a time (AC04).</summary>
    Active,

    /// <summary>Terminal for this record - retired to make way for a new activation, or revoked. Never reused.</summary>
    Deactivated,
}

public static class CardIssueStatusExtensions
{
    public static string ToWireString(this CardIssueStatus status) => status switch
    {
        CardIssueStatus.Issued => "issued",
        CardIssueStatus.Active => "active",
        CardIssueStatus.Deactivated => "deactivated",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out CardIssueStatus status)
    {
        switch (value)
        {
            case "issued": status = CardIssueStatus.Issued; return true;
            case "active": status = CardIssueStatus.Active; return true;
            case "deactivated": status = CardIssueStatus.Deactivated; return true;
            default: status = default; return false;
        }
    }
}
