namespace Biolog.Features.ParserRegistry.Model;

/// <summary>
/// ADR-0002 §4.3's fail-closed pattern, applied to parser records (BIO-DOC-005-AC05: "tenant
/// parser cannot inspect another tenant"): a parser that does not exist and a parser that exists
/// but belongs to a different tenant are indistinguishable to the caller - both raise this same
/// exception, never a 403-shaped "forbidden" that would confirm the record's existence to a
/// caller who has no right to know that. The identical choice BIO-DOC-001's
/// <c>DocumentSessionNotFoundException</c> made for cross-tenant document sessions.
/// </summary>
public sealed class ParserNotFoundException : InvalidOperationException
{
    public string ParserId { get; }

    public ParserNotFoundException(string parserId)
        : base($"parser_not_found: '{parserId}'.")
    {
        ParserId = parserId;
    }
}
