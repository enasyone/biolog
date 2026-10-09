using Biolog.Api.Tenancy;
using Biolog.Features.VisitorCredentials;
using Biolog.Features.VisitorCredentials.Api;
using Biolog.Features.VisitorCredentials.Audit;
using Biolog.Features.VisitorCredentials.Domain;
using Biolog.Features.VisitorCredentials.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Visitor check-in/out and temporary credentials (BIO-VIS-002), registered the same way every
/// sibling identity-wave module wraps its feature project.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <see cref="PermissionGatewayAdapter"/> wraps BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly - this story's RBAC integration seam
/// for its two authorization decisions (<c>VisitorCredentialService.GrantAsync</c>/<c>RevokeAsync</c>).
///
/// <see cref="VisitorRecordGatewayAdapter"/> wraps BIO-VIS-001's own already-registered
/// (internal, <c>InternalsVisibleTo Biolog.Api</c>), Scoped <c>VisitorRecordService</c> directly -
/// reachable here only because that project's own csproj grants this assembly internals
/// visibility, the same cross-story gateway pattern every sibling story uses.
/// <see cref="CardIssuanceGatewayAdapter"/> wraps BIO-CRD-005's own already-registered, Scoped
/// <c>CardIssueService</c> the same way. Both adapters translate the wrapped feature's own domain
/// error type into this feature's own <see cref="VisitorCredentialError"/> (same Code/Status/
/// Message/Detail) so <c>VisitorCredentialEndpoints</c>'s single catch clause can map it to a
/// problem response - without this translation, a BIO-VIS-001/BIO-CRD-005 validation failure
/// (e.g. "not checked in", "another card already active") would surface as an unhandled 500
/// instead of the correct 4xx.
///
/// <b>AC03 - this module's endpoint mapping takes over <c>POST /api/v1/visitors/{visitorId}/check-out</c>
/// from BIO-VIS-001's own (now-narrowed) mapping</b> - see
/// <c>Biolog.Features.VisitorCredentials.Api.VisitorCredentialEndpoints</c>'s own header and
/// <c>docs/features/bio-vis-002/README.md</c> "Why this story edits BIO-VIS-001's own endpoint
/// mapping" for the full disclosed-handoff record.
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioIdn006Module"/> (registered as
/// <c>"Rbac"</c>), <see cref="BioVis001Module"/> (registered as <c>"Visitors"</c>) and
/// <see cref="BioCrd005Module"/> (registered as <c>"CardIssuance"</c>).
/// </summary>
public sealed class BioVis002Module : IBiologModule
{
    public string Name => "VisitorCredentials";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "Rbac", "Visitors", "CardIssuance"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<IVisitorCredentialRepository, InMemoryVisitorCredentialRepository>();
        services.AddSingleton<IVisitorCredentialAuditSink, InMemoryVisitorCredentialAuditSink>();
        services.AddSingleton<VisitorCredentialLockRegistry>();
        // BIO-VIS-002-F04 (Seyi's round-3 FAIL): see IStrandedCardLedger's own header.
        services.AddSingleton<IStrandedCardLedger, InMemoryStrandedCardLedger>();

        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddScoped<IVisitorRecordGateway>(
            sp => new VisitorRecordGatewayAdapter(sp.GetRequiredService<Biolog.Features.Visitors.VisitorRecordService>()));
        services.AddScoped<ICardIssuanceGateway>(
            sp => new CardIssuanceGatewayAdapter(sp.GetRequiredService<Biolog.Features.CardIssuance.CardIssueService>()));

        services.AddScoped<VisitorCredentialService>();

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        VisitorCredentialEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }

    private sealed class VisitorRecordGatewayAdapter(Biolog.Features.Visitors.VisitorRecordService visitorRecordService) : IVisitorRecordGateway
    {
        public async Task<VisitorRecordView?> GetAsync(string visitorId, CancellationToken cancellationToken = default)
        {
            try
            {
                var record = await visitorRecordService.GetAsync(visitorId, cancellationToken);
                return MapToView(record);
            }
            catch (Biolog.Features.Visitors.Domain.VisitorError ex) when (ex.Code == "visitor_not_found")
            {
                return null;
            }
            catch (Biolog.Features.Visitors.Domain.VisitorError ex)
            {
                throw Translate(ex);
            }
        }

        public async Task<VisitorRecordView> CheckOutAsync(string visitorId, int? expectedVersion, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default)
        {
            if (!Biolog.Features.Visitors.Audit.ActorTypeExtensions.TryParse(actorTypeWire, out var actorType))
            {
                actorType = Biolog.Features.Visitors.Audit.ActorType.ServiceAccount;
            }

            try
            {
                var record = await visitorRecordService.CheckOutAsync(
                    visitorId,
                    new Biolog.Features.Visitors.Api.VersionedVisitorRequest { Version = expectedVersion },
                    new Biolog.Features.Visitors.Audit.AuditActor(actorType, actorId),
                    correlationId,
                    cancellationToken);
                return MapToView(record);
            }
            catch (Biolog.Features.Visitors.Domain.VisitorError ex)
            {
                throw Translate(ex);
            }
        }

        private static VisitorRecordView MapToView(Biolog.Features.Visitors.Domain.VisitorRecord r) => new()
        {
            Id = r.Id,
            TenantId = r.TenantId,
            PersonId = r.PersonId,
            FullName = r.FullName,
            HostApprovalStatusWire = Biolog.Features.Visitors.Domain.HostApprovalStatusExtensions.ToWireString(r.HostApprovalStatus),
            VisitStatusWire = Biolog.Features.Visitors.Domain.VisitStatusExtensions.ToWireString(r.VisitStatus),
            CheckedInAt = r.CheckedInAt,
            CheckedOutAt = r.CheckedOutAt,
            ExpiresAt = r.ExpiresAt,
            CreatedAt = r.CreatedAt,
            Version = r.Version,
        };

        private static VisitorCredentialError Translate(Biolog.Features.Visitors.Domain.VisitorError ex) =>
            new(ex.Message, ex.Code, ex.Status, ex.Detail);
    }

    private sealed class CardIssuanceGatewayAdapter(Biolog.Features.CardIssuance.CardIssueService cardIssueService) : ICardIssuanceGateway
    {
        public async Task<CardIssueView?> GetAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
        {
            var issue = await cardIssueService.GetAsync(personId, cardClass, sequenceNumber, cancellationToken);
            return issue is null ? null : MapToView(issue);
        }

        public async Task<IReadOnlyList<CardIssueView>> ListAsync(string personId, string cardClass, CancellationToken cancellationToken = default)
        {
            var issues = await cardIssueService.ListAsync(personId, cardClass, cancellationToken);
            return [.. issues.Select(MapToView)];
        }

        public async Task<CardIssueView> IssueAsync(string personId, string cardClass, string templateRef, string printerRef, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default)
        {
            try
            {
                var issue = await cardIssueService.IssueAsync(
                    personId, cardClass,
                    new Biolog.Features.CardIssuance.IssueCardRequest { TemplateRef = templateRef, PrinterRef = printerRef },
                    ResolveActor(actorId, actorTypeWire), correlationId, cancellationToken);
                return MapToView(issue);
            }
            catch (Biolog.Features.CardIssuance.Domain.CardIssueError ex)
            {
                throw Translate(ex);
            }
        }

        public async Task<CardIssueView> ActivateAsync(string personId, string cardClass, int sequenceNumber, int expectedVersion, string idempotencyKey, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default)
        {
            try
            {
                var issue = await cardIssueService.ActivateAsync(
                    personId, cardClass, sequenceNumber, expectedVersion, idempotencyKey,
                    ResolveActor(actorId, actorTypeWire), correlationId, cancellationToken);
                return MapToView(issue);
            }
            catch (Biolog.Features.CardIssuance.Domain.CardIssueError ex)
            {
                throw Translate(ex);
            }
        }

        public async Task<CardIssueView> DeactivateAsync(string personId, string cardClass, int sequenceNumber, int expectedVersion, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default)
        {
            try
            {
                var issue = await cardIssueService.DeactivateAsync(
                    personId, cardClass, sequenceNumber, expectedVersion,
                    ResolveActor(actorId, actorTypeWire), correlationId, cancellationToken);
                return MapToView(issue);
            }
            catch (Biolog.Features.CardIssuance.Domain.CardIssueError ex)
            {
                throw Translate(ex);
            }
        }

        private static Biolog.Features.CardIssuance.Audit.AuditActor ResolveActor(string actorId, string actorTypeWire)
        {
            if (!Biolog.Features.CardIssuance.Audit.ActorTypeExtensions.TryParse(actorTypeWire, out var actorType))
            {
                actorType = Biolog.Features.CardIssuance.Audit.ActorType.ServiceAccount;
            }

            return new Biolog.Features.CardIssuance.Audit.AuditActor(actorType, actorId);
        }

        private static CardIssueView MapToView(Biolog.Features.CardIssuance.Domain.CardIssue issue) => new()
        {
            PersonId = issue.PersonId,
            CardClass = issue.CardClass,
            SequenceNumber = issue.SequenceNumber,
            StatusWire = Biolog.Features.CardIssuance.Domain.CardIssueStatusExtensions.ToWireString(issue.Status),
            Version = issue.Version,
        };

        private static VisitorCredentialError Translate(Biolog.Features.CardIssuance.Domain.CardIssueError ex) =>
            new(ex.Message, ex.Code, ex.Status, ex.Detail);
    }
}
