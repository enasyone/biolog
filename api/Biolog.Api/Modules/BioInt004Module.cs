using System.Net.Http;
using Biolog.Api.Tenancy;
using Biolog.Features.Webhooks;
using Biolog.Features.Webhooks.Api;
using Microsoft.Extensions.Configuration;

namespace Biolog.Api.Modules;

/// <summary>
/// BIO-INT-004's outbound-webhook infrastructure, registered the same thin-adapter way every
/// sibling separate-project feature is (see <c>Biolog.Features.Webhooks.csproj</c>'s own
/// comment for why this project cannot reference <see cref="ITenantContextAccessor"/>,
/// BIO-IDN-006's role model, or BIO-RPT-001's event bus directly).
///
/// <b>No real cross-feature event publisher exists yet (confirmed by searching the whole
/// codebase before designing - see <c>docs/features/bio-int-004/README.md</c>).</b> The only
/// existing <c>DomainEventEnvelope</c> publisher is BIO-RPT-001's own synthetic demonstration
/// event (its <c>/api/v1/integrations/primary-action</c> endpoint). This module subscribes to
/// the SAME real, shared <see cref="Biolog.Features.Notifications.IDomainEventBus"/> that
/// demonstration event already publishes through - translating
/// <see cref="Biolog.Features.Notifications.DomainEventEnvelope"/> into this project's own
/// <see cref="WebhookEventEnvelope"/> field-by-field in <see cref="TranslateEnvelope"/> below,
/// the same host-module translation-boundary shape <c>BioDev003Module</c>'s own
/// <c>CertificationGateAdapter</c> already established for an analogous cross-feature-type
/// boundary - so a FUTURE real event publisher triggers real webhook delivery with no change
/// needed here, and today's demonstration event already proves the full pipeline end to end.
///
/// Endpoint group <c>/api/v1/integrations/webhooks</c> - tenant-scoped subscription CRUD
/// (AC01). Every route requires a resolved, provenance-verified actor
/// (<see cref="ActorResolver"/>/<see cref="ActorProvenanceGate"/>) who holds the relevant
/// permission (<see cref="WebhookSubscriptionService.ManagePermission"/>/
/// <see cref="WebhookSubscriptionService.ReadPermission"/>, checked via BIO-IDN-006's own
/// <c>PermissionService.ActorHasPermissionAsync</c>) - the same ADR-0006 Decision 3 + real RBAC
/// grant seam BIO-INT-003 already established.
/// </summary>
public sealed class BioInt004Module : IBiologModule
{
    public string Name => "Webhooks";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "Notifications", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IWebhookSubscriptionRepository, InMemoryWebhookSubscriptionRepository>();
        services.AddSingleton<IWebhookDeliveryAuditSink, InMemoryWebhookDeliveryAuditSink>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        // BIO-INT-004-ZB-01 (Zainab, High - SSRF): ConnectCallback is the real enforcement
        // point for WebhookDestinationPolicy - see WebhookConnectCallback's own header for why
        // this has to happen at connect time, not URL-parse time.
        services.AddHttpClient<IWebhookSender, HttpWebhookSender>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectCallback = WebhookConnectCallback.ConnectAsync,
            });
        services.AddSingleton<WebhookDispatchService>();

        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();

        services.AddScoped(sp => new WebhookSubscriptionService(
            sp.GetRequiredService<IWebhookSubscriptionRepository>(),
            sp.GetRequiredService<IPermissionGateway>(),
            allowInsecureHttp: configuration.GetValue<bool>("Webhooks:AllowInsecureHttp")));
    }

    public void Configure(IApplicationBuilder app)
    {
        var bus = app.ApplicationServices.GetRequiredService<Biolog.Features.Notifications.IDomainEventBus>();
        var dispatchService = app.ApplicationServices.GetRequiredService<WebhookDispatchService>();
        bus.Subscribe((envelope, cancellationToken) => dispatchService.HandleAsync(TranslateEnvelope(envelope), cancellationToken));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapWebhookEndpoints(endpoints.MapGroup("/api/v1/integrations/webhooks"));
    }

    /// <summary>Field-for-field, both copies of the same BIO-ARC-003 schema - see
    /// <see cref="WebhookEventEnvelope"/>'s own header for why this translation exists rather
    /// than a shared type. Maps each enum member explicitly by name rather than casting through
    /// the underlying int - the two enums are independently declared and nothing stops either
    /// one's member order drifting from the other, so a numeric cast would silently mistranslate
    /// instead of failing to compile (the same certainty-over-convenience reasoning
    /// <c>WebhookPayloadDto</c>'s own explicit <c>[JsonPropertyName]</c> attributes already apply
    /// to the wire boundary, applied here to this in-process one instead).</summary>
    private static WebhookEventEnvelope TranslateEnvelope(Biolog.Features.Notifications.DomainEventEnvelope source) => new(
        EventId: source.EventId,
        SchemaVersion: source.SchemaVersion,
        TenantId: source.TenantId,
        AggregateType: TranslateAggregateType(source.AggregateType),
        AggregateId: source.AggregateId,
        AggregateVersion: source.AggregateVersion,
        CorrelationId: source.CorrelationId,
        CausationId: source.CausationId,
        OccurredAt: source.OccurredAt,
        ReceivedAt: source.ReceivedAt,
        Producer: source.Producer,
        PayloadClassification: TranslateClassification(source.PayloadClassification),
        Payload: source.Payload,
        IdempotencyKey: source.IdempotencyKey);

    private static AggregateType TranslateAggregateType(Biolog.Features.Notifications.AggregateType value) => value switch
    {
        Biolog.Features.Notifications.AggregateType.Tenant => AggregateType.Tenant,
        Biolog.Features.Notifications.AggregateType.Person => AggregateType.Person,
        Biolog.Features.Notifications.AggregateType.Credential => AggregateType.Credential,
        Biolog.Features.Notifications.AggregateType.Device => AggregateType.Device,
        Biolog.Features.Notifications.AggregateType.DocumentSession => AggregateType.DocumentSession,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unmapped Notifications.AggregateType"),
    };

    private static EventClassification TranslateClassification(Biolog.Features.Notifications.EventClassification value) => value switch
    {
        Biolog.Features.Notifications.EventClassification.Public => EventClassification.Public,
        Biolog.Features.Notifications.EventClassification.Internal => EventClassification.Internal,
        Biolog.Features.Notifications.EventClassification.Personal => EventClassification.Personal,
        Biolog.Features.Notifications.EventClassification.Sensitive => EventClassification.Sensitive,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unmapped Notifications.EventClassification"),
    };

    private static void MapWebhookEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            HttpContext context, CreateWebhookSubscriptionRequest request, WebhookSubscriptionService service,
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

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp))
            {
                return Results.Problem(title: "invalid_url", detail: "url must be an absolute http(s) URL", statusCode: StatusCodes.Status400BadRequest);
            }

            var eventTypes = new HashSet<AggregateType>();
            foreach (var raw in request.EventTypes ?? [])
            {
                if (!TryParseAggregateType(raw, out var parsed))
                {
                    return Results.Problem(title: "unknown_event_type", detail: $"'{raw}' is not a known event type", statusCode: StatusCodes.Status400BadRequest);
                }

                eventTypes.Add(parsed);
            }

            try
            {
                var subscription = await service.CreateAsync(tenantId, actor!, url, eventTypes, cancellationToken);
                return Results.Created($"/api/v1/integrations/webhooks/{subscription.SubscriptionId}", ToWire(subscription));
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

        group.MapGet("/", async (
            HttpContext context, WebhookSubscriptionService service, ITenantContextAccessor tenantAccessor,
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
                var subscriptions = await service.ListAsync(tenantId, actor!, cancellationToken);
                return Results.Ok(new { data = subscriptions.Select(ToWireSummary) });
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
        });

        group.MapPut("/{id}/status", async (
            HttpContext context, string id, UpdateWebhookSubscriptionStatusRequest request, WebhookSubscriptionService service,
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
            catch (SubscriptionNotFoundException)
            {
                return Results.NotFound(new { title = "subscription_not_found" });
            }
            catch (SubscriptionConcurrencyConflictException ex)
            {
                return Results.Problem(
                    title: "concurrency_conflict", detail: ex.Message, statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["current_version"] = ex.CurrentVersion });
            }
        });
    }

    private static bool TryParseAggregateType(string raw, out AggregateType value)
    {
        switch (raw)
        {
            case "tenant": value = AggregateType.Tenant; return true;
            case "person": value = AggregateType.Person; return true;
            case "credential": value = AggregateType.Credential; return true;
            case "device": value = AggregateType.Device; return true;
            case "document_session": value = AggregateType.DocumentSession; return true;
            default: value = default; return false;
        }
    }

    private static string ToWireString(AggregateType value) => value switch
    {
        AggregateType.Tenant => "tenant",
        AggregateType.Person => "person",
        AggregateType.Credential => "credential",
        AggregateType.Device => "device",
        AggregateType.DocumentSession => "document_session",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unmapped AggregateType"),
    };

    private static object ToWire(WebhookSubscription subscription) => new
    {
        id = subscription.SubscriptionId,
        url = subscription.Url.ToString(),
        eventTypes = subscription.EventTypes.Select(ToWireString),
        enabled = subscription.Enabled,
        signingSecret = subscription.SigningSecretHex,
        version = subscription.Version,
        createdAt = subscription.CreatedAt,
    };

    /// <summary>List responses never repeat the signing secret - it is shown exactly once, at creation (the same discipline BIO-INT-003's own service-account secret already established).</summary>
    private static object ToWireSummary(WebhookSubscription subscription) => new
    {
        id = subscription.SubscriptionId,
        url = subscription.Url.ToString(),
        eventTypes = subscription.EventTypes.Select(ToWireString),
        enabled = subscription.Enabled,
        version = subscription.Version,
        createdAt = subscription.CreatedAt,
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

    /// <summary>Wraps BIO-IDN-006's own already-registered <c>PermissionService</c> - the same pattern BIO-ACC-001's/BIO-INT-003's own <c>PermissionGatewayAdapter</c> already established.</summary>
    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }
}

public sealed record CreateWebhookSubscriptionRequest(string Url, IReadOnlyList<string>? EventTypes);

public sealed record UpdateWebhookSubscriptionStatusRequest(bool Enabled, int Version);
