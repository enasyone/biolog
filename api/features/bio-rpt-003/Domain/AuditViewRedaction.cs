namespace Biolog.Features.AuditInvestigation.Domain;

/// <summary>
/// BIO-RPT-003-AC04's second, independent gate: "sensitive payload fields respect redaction
/// policy." A record's <c>Classification</c>/<c>Withheld</c>/<c>Detail</c> already reflect what the
/// PRODUCING feature decided was safe to CAPTURE (ADR-0003 Decision 3, applied at write time by
/// that feature's own local <c>*AuditEventBuilder</c>). This gate additionally decides what is safe
/// to SHOW the specific person asking, right now - an independent, view-time check, not a
/// duplicate of the producer's own gate (need-to-know is a different question from safe-to-carry).
///
/// <c>Classification</c> itself always travels unmodified, even when <c>Detail</c> is hidden - a
/// viewer without permission still learns THAT a record is sensitive, never what it contains
/// (ADR-0003: "classification is the maximum ... so a consumer can make a ... decision from the
/// envelope alone" - hiding that fact too would make the search results actively misleading about
/// what kind of record exists).
///
/// <c>Secret</c>-classified records are hidden unconditionally, regardless of permission - defence
/// in depth: no producer in this codebase ever registers <c>secret</c> (ADR-0003: "never" appears
/// in an envelope at all), so this branch should be unreachable in practice, and stays that way
/// even if a future source adapter's own bug ever let one through.
/// </summary>
public static class AuditViewRedaction
{
    public static NormalizedAuditRecord Apply(NormalizedAuditRecord record, bool canViewSensitiveDetail)
    {
        var level = AuditClassificationExtensions.TryParse(record.Classification, out var parsed) ? parsed : AuditClassification.Secret;
        var mustHide = level switch
        {
            AuditClassification.Secret => true,
            AuditClassification.Personal or AuditClassification.Sensitive => !canViewSensitiveDetail,
            _ => false,
        };

        if (!mustHide || record.Detail.Count == 0)
        {
            return record;
        }

        var withheld = new List<string>(record.Withheld);
        withheld.AddRange(record.Detail.Keys);
        withheld.Sort(StringComparer.Ordinal);

        return record with { Detail = new Dictionary<string, object?>(), Withheld = withheld };
    }
}
