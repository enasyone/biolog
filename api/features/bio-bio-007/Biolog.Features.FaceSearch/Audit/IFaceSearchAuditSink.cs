namespace Biolog.Features.FaceSearch.Audit;

/// <summary>AC05's persistence seam for <see cref="FaceSearchAuditEvent"/>. This story ships only
/// <see cref="InMemoryFaceSearchAuditSink"/>; a later story can implement this against a real
/// datastore without <c>FaceSearchService</c> changing shape. Internal - reachable only from
/// this assembly, <c>Biolog.Api</c> and this feature's own test project (BIO-IDN-001-QA-01's
/// governance shape).</summary>
internal interface IFaceSearchAuditSink
{
    Task WriteAsync(FaceSearchAuditEvent auditEvent, CancellationToken cancellationToken);

    /// <summary>ADR-0003 Decision 7, element 4: lets a caller resolve an ambiguous
    /// <see cref="WriteAsync"/> throw by checking whether the event was actually durably written,
    /// rather than assuming failure.</summary>
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken);

    /// <summary>Every audit event for one tenant. Empty (never null) when nothing has been
    /// recorded. Never returns another tenant's events.</summary>
    Task<IReadOnlyList<FaceSearchAuditEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken);
}
