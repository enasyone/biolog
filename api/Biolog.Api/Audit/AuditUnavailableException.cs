namespace Biolog.Api.Audit;

/// <summary>
/// ADR-0002 Decision 5 / ADR-0003 Decision 7: thrown when an audited effect cannot proceed
/// because its audit event could not be confirmed as recorded - either <see cref="IAuditSink.RecordAsync"/>
/// threw and a follow-up <see cref="IAuditSink.ExistsAsync"/> check confirmed the event is
/// genuinely absent, or that verification call itself failed (a total audit-store outage).
/// Either way, the caller's mutation is never committed: an unaudited effect is not an
/// acceptable substitute for a failed one.
/// </summary>
public sealed class AuditUnavailableException : Exception
{
    public AuditUnavailableException(string message)
        : base(message)
    {
    }

    public AuditUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
