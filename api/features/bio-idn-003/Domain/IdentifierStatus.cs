namespace Biolog.Features.InstitutionalIdentifiers.Domain;

/// <summary>
/// BIO-IDN-003-AC03. Deliberately just two states, one-way: an identifier record starts
/// <see cref="Active"/> at assignment and can only be administratively <see cref="Retired"/>
/// (terminal) - the record's own "governed deletion", mirroring BIO-IDN-002's
/// <c>ProfileStatus</c> exactly. Retiring never deletes the record (AC03 "history/status is
/// preserved") - a corrected or reissued identifier is a NEW record at the next
/// <see cref="InstitutionalIdentifier.SequenceNumber"/> for the same
/// <c>(PersonId, Type, CustomLabel)</c>, not an in-place edit of <see cref="InstitutionalIdentifier.Value"/>
/// - see that type's header for why <c>Value</c> is immutable.
/// </summary>
public enum IdentifierStatus
{
    Active,
    Retired,
}

public static class IdentifierStatusExtensions
{
    public static string ToWireString(this IdentifierStatus status) => status switch
    {
        IdentifierStatus.Active => "ACTIVE",
        IdentifierStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out IdentifierStatus status)
    {
        switch (value)
        {
            case "ACTIVE": status = IdentifierStatus.Active; return true;
            case "RETIRED": status = IdentifierStatus.Retired; return true;
            default: status = default; return false;
        }
    }
}
