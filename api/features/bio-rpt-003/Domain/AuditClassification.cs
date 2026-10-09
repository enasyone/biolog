namespace Biolog.Features.AuditInvestigation.Domain;

/// <summary>
/// Mirrors <c>contracts/schemas/data-classification.json</c> (ADR-0003 Decision 3) exactly: five
/// levels, ranked, <c>Secret</c> highest ("never" appears in an envelope - see that ADR's own
/// "secrets are absent, not redacted"). This story does not re-implement the recursive
/// allowlist-projection engine every producing feature's own local <c>*AuditEventBuilder</c>
/// already runs at WRITE time (a shallow, flat detail dict with a per-field classification, the
/// same right-sized local port every sibling feature uses) - this enum exists for a SECOND,
/// independent gate this story adds at VIEW time (<see cref="AuditViewRedaction"/>): the producer
/// already decided what was safe to CAPTURE, this gate additionally decides what is safe to SHOW
/// the specific person asking, right now.
/// </summary>
public enum AuditClassification
{
    Public = 0,
    Internal = 1,
    Personal = 2,
    Sensitive = 3,
    Secret = 4,
}

public static class AuditClassificationExtensions
{
    public static string ToWireString(this AuditClassification level) => level switch
    {
        AuditClassification.Public => "public",
        AuditClassification.Internal => "internal",
        AuditClassification.Personal => "personal",
        AuditClassification.Sensitive => "sensitive",
        AuditClassification.Secret => "secret",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out AuditClassification level)
    {
        switch (value)
        {
            case "public": level = AuditClassification.Public; return true;
            case "internal": level = AuditClassification.Internal; return true;
            case "personal": level = AuditClassification.Personal; return true;
            case "sensitive": level = AuditClassification.Sensitive; return true;
            case "secret": level = AuditClassification.Secret; return true;
            default: level = default; return false;
        }
    }

    /// <summary>ADR-0003: "an unregistered classification is not a classification ... treated as the most restrictive." A wire value this registry does not recognise ranks as <see cref="AuditClassification.Secret"/>, never as safe.</summary>
    public static int RankOf(string? wireValue) => TryParse(wireValue, out var level) ? (int)level : (int)AuditClassification.Secret;
}
