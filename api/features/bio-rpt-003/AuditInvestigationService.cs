using Biolog.Features.AuditInvestigation.Audit;
using Biolog.Features.AuditInvestigation.Domain;
using Biolog.Features.AuditInvestigation.Tenancy;

namespace Biolog.Features.AuditInvestigation;

/// <summary>
/// Application facade for BIO-RPT-003. AC02 ("audit records are read-only to tenant users") is
/// satisfied structurally: no method on this service, and no route in
/// <see cref="Api.AuditInvestigationEndpoints"/>, ever mutates a <see cref="NormalizedAuditRecord"/>
/// - there is nothing to call. The only write this service ever performs is its OWN meta-audit of
/// the export action (AC03), which never targets an investigation record either.
/// </summary>
internal sealed class AuditInvestigationService(
    IEnumerable<IAuditEventSource> sources,
    IAuditExportPermissionGateway permissionGateway,
    ICurrentTenantProvider tenantProvider,
    IAuditInvestigationAuditSink auditSink)
{
    public async Task<(IReadOnlyList<NormalizedAuditRecord> Records, int TotalCount)> SearchAsync(
        AuditInvestigationFilter filter, string requestingActorId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var canViewSensitive = await ResolveCanViewSensitiveAsync(requestingActorId, cancellationToken);

        var matches = Matching(tenantId, filter)
            .OrderByDescending(r => r.OccurredAt, StringComparer.Ordinal)
            .ToList();
        var total = matches.Count;

        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var paged = matches
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => AuditViewRedaction.Apply(r, canViewSensitive))
            .ToList();

        return (paged, total);
    }

    public async Task<NormalizedAuditRecord?> GetByIdAsync(
        string eventId, string requestingActorId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var record = AllForTenant(tenantId).FirstOrDefault(r => r.EventId == eventId);
        if (record is null)
        {
            return null;
        }

        var canViewSensitive = await ResolveCanViewSensitiveAsync(requestingActorId, cancellationToken);
        return AuditViewRedaction.Apply(record, canViewSensitive);
    }

    /// <summary>
    /// BIO-RPT-003-AC03: permissioned (throws <see cref="AuditInvestigationError.ExportForbidden"/>
    /// when ungranted) and audited - the CSV bytes are never returned until this method's own
    /// write to <see cref="IAuditInvestigationAuditSink"/> has settled (ADR-0003 Decision 7's
    /// ordering rule, generalised beyond denials: "that ordering applies to every audited effect").
    /// </summary>
    public async Task<(byte[] Bytes, string ContentType, string FileName)> ExportCsvAsync(
        AuditInvestigationFilter filter, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.HasPermissionAsync(actor.Id, AuditPermissionKeys.Export, cancellationToken))
        {
            throw AuditInvestigationError.ExportForbidden();
        }

        var canViewSensitive = await ResolveCanViewSensitiveAsync(actor.Id, cancellationToken);
        var matches = Matching(tenantId, filter)
            .OrderByDescending(r => r.OccurredAt, StringComparer.Ordinal)
            .Select(r => AuditViewRedaction.Apply(r, canViewSensitive))
            .ToList();

        var bytes = CsvAuditExporter.Export(matches);
        await AuditExportAsync(tenantId, actor, matches.Count, correlationId, cancellationToken);

        return (bytes, "text/csv", "audit-investigation-export.csv");
    }

    private IEnumerable<NormalizedAuditRecord> AllForTenant(string tenantId) =>
        sources.SelectMany(s => s.ListForTenant(tenantId));

    private IEnumerable<NormalizedAuditRecord> Matching(string tenantId, AuditInvestigationFilter filter) =>
        AllForTenant(tenantId).Where(r => Matches(r, filter));

    private static bool Matches(NormalizedAuditRecord record, AuditInvestigationFilter filter)
    {
        if (filter.ActorType is { } actorType && record.Actor.Type != actorType)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.ActorId) && record.Actor.Id != filter.ActorId)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.PersonId) && record.PersonId != filter.PersonId)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.Action) && record.Action != filter.Action)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.DeviceId) && record.Device?.DeviceId != filter.DeviceId)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.ReasonCode) && record.ReasonCode != filter.ReasonCode)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.OccurredFrom) && string.CompareOrdinal(record.OccurredAt, filter.OccurredFrom) < 0)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.OccurredTo) && string.CompareOrdinal(record.OccurredAt, filter.OccurredTo) > 0)
        {
            return false;
        }

        return true;
    }

    private Task<bool> ResolveCanViewSensitiveAsync(string requestingActorId, CancellationToken cancellationToken) =>
        permissionGateway.HasPermissionAsync(requestingActorId, AuditPermissionKeys.ViewSensitiveDetail, cancellationToken);

    private async Task AuditExportAsync(string tenantId, AuditActor actor, int rowCount, string correlationId, CancellationToken cancellationToken)
    {
        var target = new AuditTarget("audit_export", null);
        var detail = new Dictionary<string, (object? Value, string Classification)>
        {
            ["row_count"] = (rowCount, "internal"),
        };
        var auditEvent = AuditInvestigationAuditEventBuilder.Build(
            tenantId, actor, "audit_investigation.export", target, AuditOutcome.Allowed, "audit_export_performed",
            correlationId, causationId: null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return;
        }
        catch (Exception)
        {
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw AuditInvestigationError.AuditUnavailable();
        }

        if (!exists)
        {
            throw AuditInvestigationError.AuditUnavailable();
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw AuditInvestigationError.TenantContextMissing();
}
