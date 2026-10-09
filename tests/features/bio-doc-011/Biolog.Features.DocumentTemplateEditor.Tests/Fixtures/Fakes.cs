using Biolog.Features.DocumentTemplateEditor.Audit;
using Biolog.Features.DocumentTemplateEditor.Domain;
using Biolog.Features.DocumentTemplateEditor.Tenancy;

namespace Biolog.Features.DocumentTemplateEditor.Tests.Fixtures;

internal sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>Grants every permission by default (so existing functional tests don't need to opt in one by one) - tests that exercise ZB-DOC011-01's denial path call <see cref="Deny"/> for a specific key.</summary>
internal sealed class FakePermissionGateway : IPermissionGateway
{
    private readonly HashSet<string> _denied = [];

    public void Deny(string permissionKey) => _denied.Add(permissionKey);

    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(!_denied.Contains(permissionKey));
}

/// <summary>ZB-DOC011-02: proves <see cref="IDocumentTemplateDraftAuditSink.ExistsAsync"/> actually resolves an ambiguous write-throw - the inner sink genuinely persists the event, then the wrapper throws anyway (simulating a transient ack failure on an otherwise-successful durable write).</summary>
internal sealed class AmbiguousWriteAuditSink(InMemoryDocumentTemplateDraftAuditSink inner) : IDocumentTemplateDraftAuditSink
{
    public async Task WriteAsync(DocumentTemplateDraftAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(auditEvent, cancellationToken);
        throw new InvalidOperationException("simulated transient ack failure after a successful durable write");
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => inner.ExistsAsync(eventId, cancellationToken);
}

/// <summary>ZB-DOC011-02: a genuine, total sink failure - the write never lands and <see cref="ExistsAsync"/> truthfully reports that.</summary>
internal sealed class AlwaysFailingAuditSink : IDocumentTemplateDraftAuditSink
{
    public Task WriteAsync(DocumentTemplateDraftAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("simulated total audit sink outage");

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
