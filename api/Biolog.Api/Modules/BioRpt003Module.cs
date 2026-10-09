using Biolog.Api.Tenancy;
using Biolog.Features.AuditInvestigation;
using Biolog.Features.AuditInvestigation.Api;
using Biolog.Features.AuditInvestigation.Audit;
using Biolog.Features.AuditInvestigation.Domain;
using Biolog.Features.AuditInvestigation.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Immutable audit investigation views (BIO-RPT-003), registered the same way every sibling
/// module wraps its feature project - written directly here, in <c>Biolog.Api.Modules</c>, so the
/// dependency stays one-way.
///
/// <b>Four <see cref="IAuditEventSource"/> adapters are the real cross-story fan-in.</b> Each
/// wraps an already-accepted sibling feature's own already-public in-memory audit sink directly
/// (no <c>InternalsVisibleTo</c> needed beyond what each feature already grants to
/// <c>Biolog.Api</c> for its OTHER module) and converts that feature's own audit-envelope shape
/// into this story's canonical <see cref="NormalizedAuditRecord"/> - zero edits to any of those
/// four features' own files. Wired today: Rbac (BIO-IDN-006, this story's one declared
/// dependency), CardTemplates (BIO-CRD-008), ReportBuilder (BIO-RPT-002), CardProduction
/// (BIO-CRD-007) - see docs/features/bio-rpt-003/README.md "Which sources are wired, and why" for
/// the reasoning and for what is deliberately NOT wired yet. <see cref="IAuditEventSource"/> stays
/// open: a future module can register another source into the same DI collection without
/// touching this story's files at all.
///
/// <see cref="RbacPermissionGatewayAdapter"/> is this story's one declared cross-story permission
/// dependency, wrapping BIO-IDN-006's own <c>PermissionService.HasPermissionAsync</c> - Scoped,
/// matching that service's own registration (BIO-RPT-002's own DI-lifetime lesson applied
/// proactively: a Singleton must never capture a Scoped dependency, so this story's own service is
/// registered Scoped too).
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioIdn006Module"/> (registered as
/// <c>"Rbac"</c>), <see cref="BioCrd008Module"/> (<c>"CardTemplates"</c>),
/// <see cref="BioRpt002Module"/> (<c>"ReportBuilder"</c>) and <see cref="BioCrd007Module"/>
/// (<c>"CardProduction"</c>) so each of those modules' own singletons are already registered
/// before this module's factories resolve them.
/// </summary>
public sealed class BioRpt003Module : IBiologModule
{
    public string Name => "AuditInvestigation";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "Rbac", "CardTemplates", "ReportBuilder", "CardProduction"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddScoped<IAuditExportPermissionGateway>(
            sp => new RbacPermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));

        services.AddSingleton<IAuditEventSource>(
            sp => new RbacAuditEventSource((Biolog.Features.Rbac.Audit.InMemoryRbacAuditSink)sp.GetRequiredService<Biolog.Features.Rbac.Audit.IRbacAuditSink>()));
        services.AddSingleton<IAuditEventSource>(
            sp => new CardTemplateAuditEventSource((Biolog.Features.CardTemplates.Audit.InMemoryCardTemplateAuditSink)sp.GetRequiredService<Biolog.Features.CardTemplates.Audit.ICardTemplateAuditSink>()));
        services.AddSingleton<IAuditEventSource>(
            sp => new ReportAuditEventSource((Biolog.Features.ReportBuilder.Audit.InMemoryReportAuditSink)sp.GetRequiredService<Biolog.Features.ReportBuilder.Audit.IReportAuditSink>()));
        services.AddSingleton<IAuditEventSource>(
            sp => new ProductionAuditEventSource((Biolog.Features.CardProduction.Audit.InMemoryProductionAuditSink)sp.GetRequiredService<Biolog.Features.CardProduction.Audit.IProductionAuditSink>()));

        services.AddSingleton<IAuditInvestigationAuditSink, InMemoryAuditInvestigationAuditSink>();
        services.AddScoped<AuditInvestigationService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AuditInvestigationEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    /// <summary>
    /// Wraps BIO-IDN-006's PUBLIC <c>PermissionService.HasPermissionAsync(personId, permissionKey)</c>
    /// directly (Biolog.Api composes across feature boundaries only through public or
    /// InternalsVisibleTo-granted types, never a second copy of RBAC's own gate logic). An actor id
    /// that fails HasPermissionAsync's own validation (not a well-formed person id, or not found in
    /// the resolved tenant) is treated as "not granted" rather than let the exception propagate -
    /// the same fail-closed semantics as PermissionService's own internal, non-throwing
    /// <c>ActorHasPermissionAsync</c> counterpart, reached here through the public surface since
    /// that internal method is only reachable from within BIO-IDN-006's own assembly boundary.
    /// </summary>
    private sealed class RbacPermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IAuditExportPermissionGateway
    {
        public async Task<bool> HasPermissionAsync(string personId, string permissionKey, CancellationToken cancellationToken = default)
        {
            try
            {
                var (granted, _) = await permissionService.HasPermissionAsync(personId, permissionKey, cancellationToken);
                return granted;
            }
            catch (Biolog.Features.Rbac.Domain.RbacError)
            {
                return false;
            }
        }
    }

    /// <summary>BIO-IDN-006's own <c>InMemoryRbacAuditSink.Events</c> is not tenant-filtered (unlike ReportBuilder's own sink) - filtered here instead. <c>person_id</c> is read straight out of <c>Detail</c> when the source event carries one (role-assign/role-revoke actions do; permission-grant actions do not, since a grant applies to a role, not a person).</summary>
    private sealed class RbacAuditEventSource(Biolog.Features.Rbac.Audit.InMemoryRbacAuditSink sink) : IAuditEventSource
    {
        public string SourceFeature => "rbac";

        public IReadOnlyList<NormalizedAuditRecord> ListForTenant(string tenantId) =>
            [.. sink.Events.Where(e => e.TenantId == tenantId).Select(Convert)];

        private static NormalizedAuditRecord Convert(Biolog.Features.Rbac.Audit.RbacAuditEvent e) => new()
        {
            SourceFeature = "rbac",
            EventId = e.EventId,
            SchemaVersion = e.SchemaVersion,
            TenantId = e.TenantId,
            Actor = new AuditActor(ConvertActorType(Biolog.Features.Rbac.Audit.ActorTypeExtensions.ToWireString(e.Actor.Type)), e.Actor.Id),
            Device = null,
            PersonId = e.Detail.TryGetValue("person_id", out var personId) ? personId as string : null,
            Action = e.Action,
            Target = new AuditTarget(e.Target.Type, e.Target.Id),
            Outcome = ConvertOutcome(Biolog.Features.Rbac.Audit.AuditOutcomeExtensions.ToWireString(e.Outcome)),
            ReasonCode = e.ReasonCode,
            CorrelationId = e.CorrelationId,
            CausationId = e.CausationId,
            OccurredAt = e.OccurredAt,
            ReceivedAt = e.ReceivedAt,
            Classification = e.Classification,
            Withheld = e.Withheld,
            Detail = e.Detail,
        };
    }

    private sealed class CardTemplateAuditEventSource(Biolog.Features.CardTemplates.Audit.InMemoryCardTemplateAuditSink sink) : IAuditEventSource
    {
        public string SourceFeature => "card_templates";

        public IReadOnlyList<NormalizedAuditRecord> ListForTenant(string tenantId) =>
            [.. sink.EventsForTenant(tenantId).Select(Convert)];

        private static NormalizedAuditRecord Convert(Biolog.Features.CardTemplates.Audit.CardTemplateAuditEvent e) => new()
        {
            SourceFeature = "card_templates",
            EventId = e.EventId,
            SchemaVersion = e.SchemaVersion,
            TenantId = e.TenantId,
            Actor = new AuditActor(ConvertActorType(Biolog.Features.CardTemplates.Audit.ActorTypeExtensions.ToWireString(e.Actor.Type)), e.Actor.Id),
            Device = null,
            PersonId = null,
            Action = e.Action,
            Target = new AuditTarget(e.Target.Type, e.Target.Id),
            Outcome = ConvertOutcome(Biolog.Features.CardTemplates.Audit.AuditOutcomeExtensions.ToWireString(e.Outcome)),
            ReasonCode = e.ReasonCode,
            CorrelationId = e.CorrelationId,
            CausationId = e.CausationId,
            OccurredAt = e.OccurredAt,
            ReceivedAt = e.ReceivedAt,
            Classification = e.Classification,
            Withheld = e.Withheld,
            Detail = e.Detail,
        };
    }

    private sealed class ReportAuditEventSource(Biolog.Features.ReportBuilder.Audit.InMemoryReportAuditSink sink) : IAuditEventSource
    {
        public string SourceFeature => "report_builder";

        public IReadOnlyList<NormalizedAuditRecord> ListForTenant(string tenantId) =>
            [.. sink.EventsForTenant(tenantId).Select(Convert)];

        private static NormalizedAuditRecord Convert(Biolog.Features.ReportBuilder.Audit.ReportAuditEvent e) => new()
        {
            SourceFeature = "report_builder",
            EventId = e.EventId,
            SchemaVersion = e.SchemaVersion,
            TenantId = e.TenantId,
            Actor = new AuditActor(ConvertActorType(Biolog.Features.ReportBuilder.Audit.ActorTypeExtensions.ToWireString(e.Actor.Type)), e.Actor.Id),
            Device = null,
            PersonId = null,
            Action = e.Action,
            Target = new AuditTarget(e.Target.Type, e.Target.Id),
            Outcome = ConvertOutcome(Biolog.Features.ReportBuilder.Audit.AuditOutcomeExtensions.ToWireString(e.Outcome)),
            ReasonCode = e.ReasonCode,
            CorrelationId = e.CorrelationId,
            CausationId = e.CausationId,
            OccurredAt = e.OccurredAt,
            ReceivedAt = e.ReceivedAt,
            Classification = e.Classification,
            Withheld = e.Withheld,
            Detail = e.Detail,
        };
    }

    /// <summary>BIO-CRD-007's own <c>InMemoryProductionAuditSink.Events</c> is not tenant-filtered - filtered here instead. <c>person_id</c> is read from the production-job target's own composite key (<c>"{personId}:{cardClass}:{sequenceNumber}[:{jobSequenceNumber}]"</c>, see <c>ProductionService.AuditAsync</c>) rather than from <c>Detail</c>, since this feature never puts it there directly.</summary>
    private sealed class ProductionAuditEventSource(Biolog.Features.CardProduction.Audit.InMemoryProductionAuditSink sink) : IAuditEventSource
    {
        public string SourceFeature => "card_production";

        public IReadOnlyList<NormalizedAuditRecord> ListForTenant(string tenantId) =>
            [.. sink.Events.Where(e => e.TenantId == tenantId).Select(Convert)];

        private static NormalizedAuditRecord Convert(Biolog.Features.CardProduction.Audit.ProductionAuditEvent e) => new()
        {
            SourceFeature = "card_production",
            EventId = e.EventId,
            SchemaVersion = e.SchemaVersion,
            TenantId = e.TenantId,
            Actor = new AuditActor(ConvertActorType(Biolog.Features.CardProduction.Audit.ActorTypeExtensions.ToWireString(e.Actor.Type)), e.Actor.Id),
            Device = null,
            PersonId = ExtractPersonId(e.Target.Id),
            Action = e.Action,
            Target = new AuditTarget(e.Target.Type, e.Target.Id),
            Outcome = ConvertOutcome(Biolog.Features.CardProduction.Audit.AuditOutcomeExtensions.ToWireString(e.Outcome)),
            ReasonCode = e.ReasonCode,
            CorrelationId = e.CorrelationId,
            CausationId = e.CausationId,
            OccurredAt = e.OccurredAt,
            ReceivedAt = e.ReceivedAt,
            Classification = e.Classification,
            Withheld = e.Withheld,
            Detail = e.Detail,
        };

        private static string? ExtractPersonId(string? targetId)
        {
            if (string.IsNullOrEmpty(targetId))
            {
                return null;
            }

            var separator = targetId.IndexOf(':');
            return separator > 0 ? targetId[..separator] : null;
        }
    }

    private static ActorType ConvertActorType(string wireValue) =>
        ActorTypeExtensions.TryParse(wireValue, out var value) ? value : ActorType.Unknown;

    private static AuditOutcome ConvertOutcome(string wireValue) =>
        AuditOutcomeExtensions.TryParse(wireValue, out var value) ? value : AuditOutcome.Error;
}
