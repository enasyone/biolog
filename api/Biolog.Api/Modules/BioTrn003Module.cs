using Biolog.Api.Tenancy;
using Biolog.Features.TransportNotifications;
using Biolog.Features.TransportNotifications.Api;

namespace Biolog.Api.Modules;

/// <summary>
/// BIO-TRN-003's parent/system transport notifications, registered the same thin-adapter way
/// every sibling separate-project feature is (see
/// <c>Biolog.Features.TransportNotifications.csproj</c>'s own comment for why this project
/// cannot reference <see cref="ITenantContextAccessor"/>, bio-trn-002's own repositories, or
/// BIO-IDN-006's role model directly).
///
/// <b>No <c>AggregateType.Transport</c> value exists, and bio-trn-002 deliberately does not
/// publish to BIO-RPT-001's own <c>IDomainEventBus</c> at all</b> (see bio-trn-002's own README
/// "AC04: a feature-local notification trigger, not a BIO-RPT-001 integration"). This module
/// instead wraps bio-trn-002's own disclosed, read-only seams directly -
/// <see cref="TransportEventFeedGatewayAdapter"/> wraps BIO-TRN-002's own already-registered,
/// Singleton <c>ITransportEventRepository</c> (internal, reachable here only because that
/// project's own csproj grants this assembly internals visibility) and already-registered
/// <c>ITriggeredNotificationLog</c> (public) directly, translating both into this project's own
/// <see cref="TransportFeedItem"/> - the same host-module translation-boundary shape
/// <see cref="BioInt004Module"/>'s own <c>TranslateEnvelope</c> already established for an
/// analogous cross-feature-type boundary. <b>Read-only by construction</b> - neither wrapped
/// interface has a write member this adapter could call, so AC04 ("notifications do not change
/// transport event truth") holds structurally, not merely by discipline.
///
/// Endpoint group <c>/api/v1/transport-notifications</c> - tenant-scoped rule CRUD plus a
/// combined dispatch-and-list deliveries read (AC01/AC03). Every route requires a resolved,
/// provenance-verified actor (<see cref="ActorResolver"/>/<see cref="ActorProvenanceGate"/>) who
/// holds the relevant permission (<see cref="TransportNotificationRuleService.ManagePermission"/>/
/// <see cref="TransportNotificationRuleService.ReadPermission"/>, checked via BIO-IDN-006's own
/// <c>PermissionService.ActorHasPermissionAsync</c>) - the same ADR-0006 Decision 3 + real RBAC
/// grant seam BIO-INT-004/BIO-ATT-003 already established, unlike bio-trn-002's own notification
/// config (deliberately left open - see that type's own header for why this story's own rules are
/// a genuinely different, gated class of decision).
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioTrn002Module"/> (registered as
/// <c>"TransportEvents"</c>) and <see cref="BioIdn006Module"/> (registered as <c>"Rbac"</c>).
/// </summary>
public sealed class BioTrn003Module : IBiologModule
{
    public string Name => "TransportNotifications";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "TransportEvents", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ITransportNotificationRuleRepository, InMemoryTransportNotificationRuleRepository>();
        services.AddSingleton<ITransportNotificationDeliveryAuditSink, InMemoryTransportNotificationDeliveryAuditSink>();
        services.AddSingleton<ITransportNotificationDispatchLedger, InMemoryTransportNotificationDispatchLedger>();
        services.AddSingleton<IInAppNotificationSender, InMemoryInAppNotificationSender>();

        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddScoped<ITransportEventFeedGateway>(
            sp => new TransportEventFeedGatewayAdapter(
                sp.GetRequiredService<Biolog.Features.TransportEvents.Domain.ITransportEventRepository>(),
                sp.GetRequiredService<Biolog.Features.TransportEvents.Domain.ITriggeredNotificationLog>()));
        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();

        services.AddScoped<TransportNotificationRuleService>();
        services.AddScoped<TransportNotificationDispatchService>();
    }

    /// <summary>
    /// Same "seed data, not provisioning logic" shape <c>DemoSessionIdentitySeed</c>/
    /// <c>BioAtt003Module</c> already established: fail-closed/inert unless
    /// <c>WebSessionBridge:SyntheticTenantId</c> is configured, idempotent, writes directly to the
    /// repository (never through <c>PermissionService.SetGrantAsync</c>, which expects a real
    /// administrative actor and audit trail a fixed startup fixture has neither of).
    /// </summary>
    public void Configure(IApplicationBuilder app)
    {
        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var tenantId = configuration["WebSessionBridge:SyntheticTenantId"];
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        var grants = app.ApplicationServices.GetRequiredService<Biolog.Features.Rbac.Domain.IRolePermissionGrantRepository>();
        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.TenantAdmin, TransportNotificationRuleService.ManagePermission);
        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.TenantAdmin, TransportNotificationRuleService.ReadPermission);
    }

    private static void SeedGrant(Biolog.Features.Rbac.Domain.IRolePermissionGrantRepository grants, string tenantId, Biolog.Features.Rbac.Domain.RoleType role, string permissionKey)
    {
        if (grants.GetAsync(tenantId, role, permissionKey).GetAwaiter().GetResult() is not null)
        {
            return;
        }

        var now = Biolog.Features.Rbac.Identifiers.UtcInstant.Now();
        grants.UpsertAsync(new Biolog.Features.Rbac.Domain.RolePermissionGrant(tenantId, role, permissionKey)
        {
            Granted = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        }).GetAwaiter().GetResult();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapTransportNotificationEndpoints(endpoints.MapGroup("/api/v1/transport-notifications"));
    }

    private static void MapTransportNotificationEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/rules", async (
            HttpContext context, CreateTransportNotificationRuleRequest request, TransportNotificationRuleService service,
            ITenantContextAccessor tenantAccessor, ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            var eventTypes = new HashSet<TransportNotificationEventType>();
            foreach (var raw in request.EventTypes ?? [])
            {
                if (!TransportNotificationEventTypeExtensions.TryParse(raw, out var parsed))
                {
                    return Results.Problem(title: "unknown_event_type", detail: $"'{raw}' is not a known event type", statusCode: StatusCodes.Status400BadRequest);
                }

                eventTypes.Add(parsed);
            }

            try
            {
                var rule = await service.CreateAsync(tenantId, actor!, request.Name, eventTypes, cancellationToken);
                return Results.Created($"/api/v1/transport-notifications/rules/{rule.RuleId}", ToWire(rule));
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(title: "invalid_request", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapGet("/rules", async (
            HttpContext context, TransportNotificationRuleService service, ITenantContextAccessor tenantAccessor,
            ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            try
            {
                var rules = await service.ListAsync(tenantId, actor!, cancellationToken);
                return Results.Ok(new { data = rules.Select(ToWire) });
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
        });

        group.MapPut("/rules/{id}/status", async (
            HttpContext context, string id, UpdateTransportNotificationRuleStatusRequest request, TransportNotificationRuleService service,
            ITenantContextAccessor tenantAccessor, ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            try
            {
                var updated = await service.SetEnabledAsync(tenantId, actor!, id, request.Enabled, request.Version, cancellationToken);
                return Results.Ok(ToWire(updated));
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
            catch (RuleNotFoundException)
            {
                return Results.NotFound(new { title = "rule_not_found" });
            }
            catch (RuleConcurrencyConflictException ex)
            {
                return Results.Problem(
                    title: "concurrency_conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["current_version"] = ex.CurrentVersion });
            }
        });

        // AC03 - this single read is also the write path: it dispatches any newly-matching rule
        // against the current bio-trn-002 feed before returning the full durable trail (see
        // TransportNotificationDispatchService's own header for why).
        group.MapGet("/deliveries", async (
            HttpContext context, TransportNotificationDispatchService service, ITenantContextAccessor tenantAccessor,
            ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            try
            {
                var deliveries = await service.DispatchAndListAsync(tenantId, actor!, cancellationToken);
                return Results.Ok(new { data = deliveries.Select(ToWire) });
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
        });
    }

    private static object ToWire(TransportNotificationRule rule) => new
    {
        id = rule.RuleId,
        name = rule.Name,
        eventTypes = rule.EventTypes.Select(t => t.ToWireString()),
        enabled = rule.Enabled,
        version = rule.Version,
        createdAt = rule.CreatedAt,
    };

    private static object ToWire(TransportNotificationDeliveryRecord delivery) => new
    {
        deliveryId = delivery.DeliveryId,
        ruleId = delivery.RuleId,
        sourceEventId = delivery.SourceEventId,
        personId = delivery.PersonId,
        routeId = delivery.RouteId,
        eventType = delivery.EventType.ToWireString(),
        occurredAt = delivery.OccurredAt,
        outcome = delivery.Outcome switch
        {
            DeliveryAttemptOutcome.Delivered => "delivered",
            DeliveryAttemptOutcome.DeadLettered => "dead_lettered",
            _ => throw new ArgumentOutOfRangeException(nameof(delivery), delivery.Outcome, "unmapped DeliveryAttemptOutcome"),
        },
        attemptCount = delivery.AttemptCount,
        detail = delivery.Detail,
        recordedAt = delivery.RecordedAt,
    };

    private static (AuditActor? Actor, IResult? Problem) ResolveActor(HttpContext context, ActorProvenanceGate provenanceGate)
    {
        try
        {
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);
            return (actor, null);
        }
        catch (ActorRequiredException ex)
        {
            return (null, Results.Problem(title: "actor_required", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }
        catch (InvalidActorTypeException ex)
        {
            return (null, Results.Problem(title: "invalid_actor_type", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }
        catch (ActorProvenanceRequiredException ex)
        {
            return (null, Results.Problem(title: "actor_provenance_required", detail: ex.Message, statusCode: StatusCodes.Status401Unauthorized));
        }
    }

    private static string RequireTenant(ITenantContextAccessor tenantAccessor) =>
        tenantAccessor.Context?.TenantId ?? throw new TenantContextMissingException();

    private static IResult TenantContextProblem(TenantContextMissingException ex) => Results.Problem(
        title: "tenant_context_missing", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);

    private static IResult PermissionProblem(PermissionRequiredException ex) => Results.Problem(
        title: "permission_required",
        detail: ex.Message,
        statusCode: StatusCodes.Status403Forbidden,
        extensions: new Dictionary<string, object?> { ["permission_key"] = ex.PermissionKey });

    /// <summary>Wraps BIO-IDN-006's own already-registered <c>PermissionService</c> - the same pattern BIO-ACC-001's/BIO-INT-003's/BIO-INT-004's own <c>PermissionGatewayAdapter</c> already established.</summary>
    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }

    /// <summary>
    /// AC04's own read-only cross-story seam onto bio-trn-002 - see this module's own header.
    /// Classifies each raw <c>TransportEvent</c> as Board/Alight by its own <c>Mode</c>, and
    /// ADDITIONALLY as Exception when every evidence entry is unverified (this story's own new,
    /// additive classification - see <see cref="TransportNotificationEventType"/>'s own header).
    /// Classifies each <c>TriggeredNotification</c> (bio-trn-002's own already-computed
    /// missing-alight-exception condition) as MissedAlight - its own <c>Id</c> is used as this
    /// story's <c>SourceEventId</c> (a distinct identity space from the underlying
    /// <c>TransportEvent.EventId</c>, deliberately: the same physical event can legitimately
    /// appear once as a Board/Alight/Exception item AND once as a MissedAlight item, each with
    /// its own independent dispatch-ledger claim). No <c>RouteId</c> is available on a
    /// <c>TriggeredNotification</c>, so MissedAlight items carry a null one - never looked up via
    /// an extra round trip, kept simple.
    /// </summary>
    private sealed class TransportEventFeedGatewayAdapter(
        Biolog.Features.TransportEvents.Domain.ITransportEventRepository eventRepository,
        Biolog.Features.TransportEvents.Domain.ITriggeredNotificationLog triggeredNotificationLog) : ITransportEventFeedGateway
    {
        public async Task<IReadOnlyList<TransportFeedItem>> ListRecentAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var items = new List<TransportFeedItem>();

            var events = await eventRepository.ListForTenantAsync(tenantId, cancellationToken);
            foreach (var e in events)
            {
                var occurredAt = DateTimeOffset.Parse(e.OccurredAt);
                var eventType = e.Mode == Biolog.Features.TransportEvents.Domain.TransportEventMode.Board
                    ? TransportNotificationEventType.Board
                    : TransportNotificationEventType.Alight;

                items.Add(new TransportFeedItem
                {
                    SourceEventId = e.EventId,
                    PersonId = e.PersonId,
                    RouteId = e.RouteId,
                    VehicleId = e.VehicleId,
                    Location = e.Location,
                    EventType = eventType,
                    OccurredAt = occurredAt,
                });

                if (e.Evidence.Count > 0 && e.Evidence.All(ev => !ev.Verified))
                {
                    items.Add(new TransportFeedItem
                    {
                        SourceEventId = e.EventId,
                        PersonId = e.PersonId,
                        RouteId = e.RouteId,
                        VehicleId = e.VehicleId,
                        Location = e.Location,
                        EventType = TransportNotificationEventType.Exception,
                        OccurredAt = occurredAt,
                    });
                }
            }

            var triggered = await triggeredNotificationLog.ListForTenantAsync(tenantId, cancellationToken);
            foreach (var t in triggered)
            {
                items.Add(new TransportFeedItem
                {
                    SourceEventId = t.Id,
                    PersonId = t.PersonId,
                    RouteId = null,
                    VehicleId = null,
                    Location = null,
                    EventType = TransportNotificationEventType.MissedAlight,
                    OccurredAt = DateTimeOffset.Parse(t.TriggeredAt),
                });
            }

            return items;
        }
    }
}

public sealed record CreateTransportNotificationRuleRequest(string Name, IReadOnlyList<string>? EventTypes);

public sealed record UpdateTransportNotificationRuleStatusRequest(bool Enabled, int Version);
