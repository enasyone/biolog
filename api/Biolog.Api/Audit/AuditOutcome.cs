namespace Biolog.Api.Audit;

/// <summary>The three outcomes ADR-0003 Decision 1 requires every audit event to carry -
/// including on success, so "no record" never has to mean either "did not happen" or
/// "happened and was allowed."</summary>
public enum AuditOutcome
{
    Allowed,
    Denied,
    Error,
}
