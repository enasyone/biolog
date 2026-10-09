using System.Text.Json;
using Biolog.Features.CardLifecycle.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.CardLifecycle.Api;

/// <summary>
/// Two route groups (ADR-0004 §1.1 versioning): card-specific actions nested under the owning
/// person/card-issue, matching BIO-CRD-005's own <c>/api/v1/persons/{personId}/card-issues/{cardClass}</c>
/// convention, and tenant-level actions under <c>/api/v1/card-lifecycle</c> (the deny-list feed,
/// offline presentation-event intake, and recovery policy - none of these are about one
/// specific card issue). Every route requires an asserted actor.
///
/// <c>ActivateReplacement</c> requires <c>Idempotency-Key</c> (a card activation, ADR-0004
/// §4.3), and so does the offline <c>PresentationEvents</c> intake (ADR-0004 §4.3's own named
/// "offline sync intake").
/// </summary>
public static class CardLifecycleEndpoints
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    private const string CardBasePath = "/api/v1/persons/{personId}/card-issues/{cardClass}";
    private const string TenantBasePath = "/api/v1/card-lifecycle";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var cardGroup = endpoints.MapGroup(CardBasePath);
        cardGroup.MapPost("/{sequenceNumber:int}/report", ReportAsync);
        cardGroup.MapPost("/{sequenceNumber:int}/recover", RecoverAsync);
        cardGroup.MapPost("/replace", ReplaceAsync);
        cardGroup.MapPost("/{sequenceNumber:int}/activate-replacement", ActivateReplacementAsync);
        cardGroup.MapGet("/{sequenceNumber:int}/lifecycle", GetLifecycleAsync);
        cardGroup.MapPost("/{sequenceNumber:int}/presentation-check", PresentationCheckAsync);

        var tenantGroup = endpoints.MapGroup(TenantBasePath);
        tenantGroup.MapGet("/deny-list", DenyListAsync);
        tenantGroup.MapPost("/presentation-events", IntakePresentationEventAsync);
        tenantGroup.MapPut("/recovery-policy", SetRecoveryPolicyAsync);
    }

    private static Task ReportAsync(HttpContext context, CardLifecycleService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<ReportCardRequest>(context);
        var record = await service.ReportAsync(personId, cardClass, sequenceNumber, request.ReasonCode, request.Reason, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardLifecycleRecordResponse.From(record));
    });

    private static Task RecoverAsync(HttpContext context, CardLifecycleService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CardLifecycleStepRequest>(context);
        var record = await service.RecoverAsync(personId, cardClass, sequenceNumber, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardLifecycleRecordResponse.From(record));
    });

    private static Task ReplaceAsync(HttpContext context, CardLifecycleService service, string personId, string cardClass) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var wire = await ReadJsonAsync<ReplacementCardWireRequest>(context);
        var request = new ReplacementCardRequest
        {
            TemplateRef = wire.TemplateRef,
            PhotoRef = wire.PhotoRef,
            LogoRef = wire.LogoRef,
            SignatureRef = wire.SignatureRef,
            PrinterRef = wire.PrinterRef,
        };
        var snapshot = await service.ReplaceAsync(personId, cardClass, request, actor, correlationId, context.RequestAborted);
        return Results.Created($"/api/v1/persons/{personId}/card-issues/{cardClass}/{snapshot.SequenceNumber}", CardIssueSnapshotResponse.From(snapshot));
    });

    private static Task ActivateReplacementAsync(HttpContext context, CardLifecycleService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CardLifecycleStepRequest>(context);
        var idempotencyKey = context.Request.Headers[IdempotencyKeyHeaderName].ToString();

        var snapshot = await service.ActivateReplacementAsync(personId, cardClass, sequenceNumber, request.ExpectedVersion, idempotencyKey, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardIssueSnapshotResponse.From(snapshot));
    });

    private static Task GetLifecycleAsync(HttpContext context, CardLifecycleService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async _ =>
    {
        var record = await service.GetRecordAsync(personId, cardClass, sequenceNumber, context.RequestAborted)
            ?? throw CardLifecycleError.RecordNotFound(personId, cardClass, sequenceNumber);
        return Results.Ok(CardLifecycleRecordResponse.From(record));
    });

    private static Task PresentationCheckAsync(HttpContext context, CardLifecycleService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<PresentationCheckRequest>(context);
        var result = await service.CheckPresentationAsync(personId, cardClass, sequenceNumber, request.DeviceRef, actor, context.RequestAborted);
        return Results.Ok(PresentationCheckResponse.From(result));
    });

    private static Task DenyListAsync(HttpContext context, CardLifecycleService service, IDeviceAuthenticator deviceAuthenticator) => Handle(context, async _ =>
    {
        // TD-CRD-006-DENY-LIST-DEVICE-AUTH: this feed exists for an offline reader to pull
        // before going offline (AC05) - a device, not a logged-in operator - so it is gated by
        // device-secret authentication (ADR-0002 Decision 7), not ActorResolver's asserted-actor
        // header every other endpoint on this route table uses. See IDeviceAuthenticator's own
        // header for the full remediation.
        if (await deviceAuthenticator.TryAuthenticateAsync(context.Request, context.RequestAborted) is null)
        {
            throw CardLifecycleError.DeviceAuthenticationFailed();
        }

        var since = context.Request.Query["since"].ToString();
        var records = await service.GetDenyListAsync(string.IsNullOrWhiteSpace(since) ? null : since, context.RequestAborted);
        return Results.Ok(new CardLifecycleDenyListResponse { Data = [.. records.Select(CardLifecycleRecordResponse.From)] });
    });

    private static Task IntakePresentationEventAsync(HttpContext context, CardLifecycleService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<PresentationEventIntakeRequest>(context);
        var idempotencyKey = context.Request.Headers[IdempotencyKeyHeaderName].ToString();

        if (!PresentationOutcomeExtensions.TryParse(request.Outcome, out var outcome))
        {
            throw CardLifecycleError.Validation([new ValidationErrorItem("outcome", "invalid_format", "outcome must be 'allowed' or 'denied'")]);
        }

        var evt = await service.IntakePresentationEventAsync(
            request.PersonId, request.CardClass, request.SequenceNumber, outcome, request.ReasonCode, request.OccurredAt, request.DeviceRef,
            idempotencyKey, actor, context.RequestAborted);
        return Results.Ok(PresentationEventResponse.From(evt));
    });

    private static Task SetRecoveryPolicyAsync(HttpContext context, CardLifecycleService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request); // require an asserted actor for this admin action too, even though attribution is not yet persisted anywhere for policy changes
        var request = await ReadJsonAsync<RecoveryPolicyRequest>(context);
        service.SetRecoveryPolicy(request.ReasonCode, request.RecoveryPermitted);
        return Results.NoContent();
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, CardLifecycleJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw CardLifecycleError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw CardLifecycleError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
        }
    }

    private static async Task Handle(HttpContext context, Func<string, Task<IResult>> action)
    {
        var correlationId = CorrelationIdHeader.Resolve(context.Request);
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;

        try
        {
            var result = await action(correlationId);
            await result.ExecuteAsync(context);
        }
        catch (CardLifecycleError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
