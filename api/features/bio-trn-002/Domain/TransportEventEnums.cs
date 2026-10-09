namespace Biolog.Features.TransportEvents.Domain;

/// <summary>AC01 - the kind of physical occurrence a raw board/alight event represents.</summary>
public enum TransportEventMode
{
    Board,
    Alight,
}

public static class TransportEventModeExtensions
{
    public static string ToWireString(this TransportEventMode mode) => mode switch
    {
        TransportEventMode.Board => "board",
        TransportEventMode.Alight => "alight",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out TransportEventMode mode)
    {
        switch (value)
        {
            case "board": mode = TransportEventMode.Board; return true;
            case "alight": mode = TransportEventMode.Alight; return true;
            default: mode = default; return false;
        }
    }
}

/// <summary>
/// AC01's "identity evidence" - the user story's own wording is "configurable UHF/NFC/face
/// COMBINATIONS" (plural, combined), which is why <see cref="TransportEvent.Evidence"/> is a
/// list of these rather than a single value (unlike BIO-ATT-001's own single-evidence
/// <c>VerificationEvidence</c>) - a real capture may combine, e.g., a UHF tag read with a face
/// match for higher confidence, and both entries belong on the same event.
/// </summary>
public enum EvidenceMethod
{
    Uhf,
    Nfc,
    Face,
    Manual,
}

public static class EvidenceMethodExtensions
{
    public static string ToWireString(this EvidenceMethod method) => method switch
    {
        EvidenceMethod.Uhf => "uhf",
        EvidenceMethod.Nfc => "nfc",
        EvidenceMethod.Face => "face",
        EvidenceMethod.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out EvidenceMethod method)
    {
        switch (value)
        {
            case "uhf": method = EvidenceMethod.Uhf; return true;
            case "nfc": method = EvidenceMethod.Nfc; return true;
            case "face": method = EvidenceMethod.Face; return true;
            case "manual": method = EvidenceMethod.Manual; return true;
            default: method = default; return false;
        }
    }
}

/// <summary>
/// AC03 - a derived (never persisted) read-time classification of a person's presence on a given
/// calendar date, computed by <see cref="TransportPresenceEvaluator"/> from a set of raw
/// <see cref="TransportEvent"/>s - the same "refuse auto-flip, compute in effect at read time"
/// discipline BIO-IDN-002/BIO-ATT-001/BIO-TRN-001 established, applied fresh here. A genuinely
/// distinct type from anything persisted on <see cref="TransportEvent"/> - nothing stored is ever
/// mutated by this evaluation.
/// </summary>
public enum DerivedPresenceStatus
{
    NoActivity,
    OnBoard,
    Alighted,
    MissingAlightException,
}

public static class DerivedPresenceStatusExtensions
{
    public static string ToWireString(this DerivedPresenceStatus status) => status switch
    {
        DerivedPresenceStatus.NoActivity => "no_activity",
        DerivedPresenceStatus.OnBoard => "on_board",
        DerivedPresenceStatus.Alighted => "alighted",
        DerivedPresenceStatus.MissingAlightException => "missing_alight_exception",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}
