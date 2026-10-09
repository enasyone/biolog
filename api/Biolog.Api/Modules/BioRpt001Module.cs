using Biolog.Api.Tenancy;
using Biolog.Features.Notifications;
using Biolog.Features.Notifications.Adapters;

namespace Biolog.Api.Modules;

/// <summary>
/// BIO-RPT-001's notification event/rule foundation, registered into this host the same
/// thin-adapter way every sibling separate-project feature is (see
/// <c>Biolog.Features.Notifications.csproj</c>'s own comment for why the project itself
/// cannot reference <see cref="ITenantContextAccessor"/> directly).
///
/// Three endpoint groups:
/// - <c>/api/v1/notification-rules</c> (tenant-scoped CRUD, BIO-RPT-001-AC01's "configurable
///   notification rules").
/// - <c>/api/v1/reporting/primary-action</c> and <c>/api/v1/reporting/view-audit-trail</c>
///   (BIO-RPT-001-AC05): the EXACT two paths `web/features/reporting/live.ts` (BIO-UX-005)
///   already targets for its <c>ReportingService</c> module - built for `/reports`, `/audit`
///   and the Dashboard's "View metrics" action specifically so a future BIO-RPT-* story
///   would have something real to connect to (see that page's own doc comment: "No BIO-RPT-*
///   backend story is built yet ... not a gap"). No change to any file under
///   <c>web/features/reporting/**</c> or <c>web/app/{reports,audit}/**</c> was needed or made -
///   see docs/features/bio-rpt-001/README.md's "AC05" section for what "live" means here and
///   its limits.
///
/// On startup (<see cref="Configure"/>), subscribes <see cref="NotificationDispatchService.HandleAsync"/>
/// to the singleton <see cref="IDomainEventBus"/> - BIO-RPT-001-AC01's actual subscription
/// mechanism - so any future story that publishes a real domain event through this same bus
/// triggers rule evaluation with no change here.
/// </summary>
public sealed class BioRpt001Module : IBiologModule
{
    public string Name => "Notifications";
    public IReadOnlyCollection<string> DependsOn => ["Tenant"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDomainEventBus, InMemoryDomainEventBus>();
        services.AddSingleton<INotificationRuleRepository, InMemoryNotificationRuleRepository>();
        services.AddSingleton<INotificationDeliveryAuditSink, InMemoryNotificationDeliveryAuditSink>();

        services.AddSingleton<INotificationChannelAdapter, EmailChannelAdapter>();
        services.AddSingleton<INotificationChannelAdapter, SmsChannelAdapter>();
        services.AddSingleton<INotificationChannelAdapter, PushChannelAdapter>();
        services.AddSingleton<INotificationChannelAdapter, TeamsChannelAdapter>();
        services.AddSingleton<INotificationChannelAdapter, WebhookChannelAdapter>();

        services.AddSingleton<NotificationDispatchService>();
    }

    public void Configure(IApplicationBuilder app)
    {
        var bus = app.ApplicationServices.GetRequiredService<IDomainEventBus>();
        var dispatchService = app.ApplicationServices.GetRequiredService<NotificationDispatchService>();
        bus.Subscribe((envelope, cancellationToken) => dispatchService.HandleAsync(envelope, cancellationToken));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapRuleEndpoints(endpoints.MapGroup("/api/v1/notification-rules"));
        MapReportingEndpoints(endpoints.MapGroup("/api/v1/reporting"));
    }

    private static void MapRuleEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            NotificationRuleRegistrationRequest request, INotificationRuleRepository repo, ITenantContextAccessor tenantAccessor,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var rule = await repo.CreateAsync(RequireTenant(tenantAccessor), request, cancellationToken);
                return Results.Created($"/api/v1/notification-rules/{rule.RuleId}", rule);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
        });

        group.MapGet("/", async (INotificationRuleRepository repo, ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await repo.ListAsync(RequireTenant(tenantAccessor), cancellationToken));
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
        });

        group.MapGet("/{ruleId}", async (
            string ruleId, INotificationRuleRepository repo, ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            try
            {
                var rule = await repo.GetAsync(RequireTenant(tenantAccessor), ruleId, cancellationToken);
                return rule is null ? Results.NotFound() : Results.Ok(rule);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
        });

        group.MapPatch("/{ruleId}/status", async (
            string ruleId, NotificationRuleStatusChangeRequest request, INotificationRuleRepository repo,
            ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await repo.UpdateStatusAsync(RequireTenant(tenantAccessor), ruleId, request, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (NotificationRuleConcurrencyConflictException ex)
            {
                return Results.Problem(
                    title: "concurrency_conflict",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["current_version"] = ex.CurrentVersion });
            }
        });
    }

    private static void MapReportingEndpoints(RouteGroupBuilder group)
    {
        // BIO-RPT-001-AC05: the exact path `reportingLivePort` ("Run report" / Dashboard
        // "View metrics") already targets. Publishes a synthetic demonstration domain event
        // through the real IDomainEventBus/NotificationDispatchService pipeline - the real
        // AC01-AC04 mechanism, not a hardcoded response - and summarizes what happened.
        group.MapPost("/primary-action", async (
            IDomainEventBus bus, INotificationDeliveryAuditSink auditSink, ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            var now = DateTimeOffset.UtcNow;
            var envelope = new DomainEventEnvelope(
                EventId: $"evt_{Guid.CreateVersion7()}",
                SchemaVersion: 1,
                TenantId: tenantId,
                AggregateType: AggregateType.Device,
                AggregateId: $"dev_{Guid.CreateVersion7()}",
                AggregateVersion: 1,
                CorrelationId: $"evt_{Guid.CreateVersion7()}",
                CausationId: null,
                OccurredAt: now,
                ReceivedAt: now,
                Producer: "reporting.primary-action",
                PayloadClassification: EventClassification.Internal,
                Payload: new Dictionary<string, string>
                {
                    ["action"] = "status_changed",
                    ["summary"] = "Demonstration device event dispatched via BIO-RPT-001's notification foundation.",
                    ["reference"] = now.ToUnixTimeSeconds().ToString(),
                });

            var before = (await auditSink.ListForTenantAsync(tenantId, cancellationToken)).Count;
            await bus.PublishAsync(envelope, cancellationToken);
            var after = await auditSink.ListForTenantAsync(tenantId, cancellationToken);
            var newRecords = after.Count - before;

            return Results.Ok(new { message = $"Evaluated notification rules for 1 demonstration event: {newRecords} delivery attempt(s) recorded." });
        });

        // BIO-RPT-001-AC05: the exact path `reportingViewAuditTrailLivePort` ("View audit
        // trail") already targets - summarizes this tenant's REAL delivery audit trail
        // (BIO-RPT-001-AC03), not synthetic UI data.
        group.MapPost("/view-audit-trail", async (
            INotificationDeliveryAuditSink auditSink, ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            string tenantId;
            try
            {
                tenantId = RequireTenant(tenantAccessor);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }

            var records = await auditSink.ListForTenantAsync(tenantId, cancellationToken);
            var delivered = records.Count(r => r.Outcome == DeliveryAttemptOutcome.Delivered);
            var deadLettered = records.Count(r => r.Outcome == DeliveryAttemptOutcome.DeadLettered);
            var deduplicated = records.Count(r => r.Outcome == DeliveryAttemptOutcome.Deduplicated);

            return Results.Ok(new
            {
                message = $"{records.Count} delivery attempt(s) recorded for this tenant: {delivered} delivered, {deadLettered} dead-lettered, {deduplicated} deduplicated.",
            });
        });
    }

    private static string RequireTenant(ITenantContextAccessor tenantAccessor) =>
        tenantAccessor.Context?.TenantId ?? throw new TenantContextMissingException();

    private static IResult TenantContextProblem(TenantContextMissingException ex) => Results.Problem(
        title: "tenant_context_missing", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
}
