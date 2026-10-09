using System.Text.Json;
using Biolog.Features.AttendanceSessions.Demo;
using Biolog.Features.AttendanceSessions.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.AttendanceSessions.Api;

/// <summary>
/// <c>/api/v1/attendance-sessions/*</c> (ADR-0004 S1.1 versioning) - tenant-plane, except the two
/// <c>primary-action</c>/<c>flag-exception</c> bridge routes this feature TAKES OVER from
/// BIO-ATT-001 under its own already-approved <c>/api/v1/attendance/*</c> path (AC05 - see
/// <see cref="AttendanceSessionDemoBridgeService"/>'s own header for why those two stay
/// tenant/actor-free by design, and <c>docs/features/bio-att-002/README.md</c> "Why this story
/// edits BIO-ATT-001's own endpoint mapping" for the disclosed handoff).
///
/// <b>Session/roster CRUD and the roster read (<see cref="CreateSessionAsync"/>,
/// <see cref="UpdateRosterAsync"/>, <see cref="ListSessionsAsync"/>, <see cref="GetSessionAsync"/>,
/// <see cref="GetRosterAsync"/>) require an asserted actor (audit attribution) but no provenance
/// proof and no permission check - ADR-0006 Decision 2 (see <see cref="SessionService"/>'s header
/// for the full re-derivation).</b> <see cref="SetPolicyAsync"/> and <see cref="CreateAdjustmentAsync"/>
/// are this feature's two authorization decisions and require both a permission grant and a
/// verified ADR-0006 Decision 3 provenance proof.
/// </summary>
public static class SessionEndpoints
{
    private const string BasePath = "/api/v1/attendance-sessions";
    private const string BridgeBasePath = "/api/v1/attendance";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapPost("/sessions", CreateSessionAsync);
        group.MapGet("/sessions", ListSessionsAsync);
        group.MapGet("/sessions/{sessionId}", GetSessionAsync);
        group.MapPut("/sessions/{sessionId}/roster", UpdateRosterAsync);
        group.MapGet("/sessions/{sessionId}/roster", GetRosterAsync);
        group.MapPost("/sessions/{sessionId}/adjustments", CreateAdjustmentAsync);
        group.MapGet("/policy", GetPolicyAsync);
        group.MapPut("/policy", SetPolicyAsync);

        // AC05 - takes over web/features/attendance/live.ts's two existing routes from
        // BIO-ATT-001's own bridge (disclosed handoff, that story's own README already named the
        // placeholder as standing in for exactly this capability).
        var bridge = endpoints.MapGroup(BridgeBasePath);
        bridge.MapPost("/primary-action", PrimaryActionAsync);
        bridge.MapPost("/flag-exception", FlagExceptionAsync);
    }

    private static Task CreateSessionAsync(HttpContext context, SessionService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CreateSessionRequest>(context);
        var session = await service.CreateAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Ok(SessionResponse.From(session));
    });

    private static Task ListSessionsAsync(HttpContext context, SessionService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var sessions = await service.ListForTenantAsync(context.RequestAborted);
        return Results.Ok(new SessionListResponse { Data = [.. sessions.Select(SessionResponse.From)] });
    });

    private static Task GetSessionAsync(HttpContext context, SessionService service, string sessionId) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var session = await service.GetAsync(sessionId, context.RequestAborted) ?? throw SessionError.SessionNotFound(sessionId);
        return Results.Ok(SessionResponse.From(session));
    });

    private static Task UpdateRosterAsync(HttpContext context, SessionService service, string sessionId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<UpdateSessionRosterRequest>(context);
        var session = await service.UpdateRosterAsync(sessionId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(SessionResponse.From(session));
    });

    private static Task GetRosterAsync(HttpContext context, SessionAttendanceQueryService service, string sessionId) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var (session, roster) = await service.GetRosterAsync(sessionId, context.RequestAborted);
        return Results.Ok(new SessionRosterResponse
        {
            Session = SessionResponse.From(session),
            Roster = [.. roster.Select(SessionRosterMemberResponse.From)],
        });
    });

    private static Task CreateAdjustmentAsync(HttpContext context, AttendanceAdjustmentService service, ActorProvenanceGate provenanceGate, string sessionId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<CreateAttendanceAdjustmentRequest>(context);
        var adjustment = await service.CreateAdjustmentAsync(sessionId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(AttendanceAdjustmentResponse.From(adjustment));
    });

    private static Task GetPolicyAsync(HttpContext context, SessionAttendancePolicyService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var sessionType = context.Request.Query["session_type"].ToString();
        var policy = await service.GetAsync(sessionType, context.RequestAborted);
        return Results.Ok(SessionAttendancePolicyResponse.From(policy));
    });

    private static Task SetPolicyAsync(HttpContext context, SessionAttendancePolicyService service, ActorProvenanceGate provenanceGate) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var sessionType = context.Request.Query["session_type"].ToString();
        var request = await ReadJsonAsync<SetSessionAttendancePolicyRequest>(context);
        var policy = await service.SetAsync(sessionType, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(SessionAttendancePolicyResponse.From(policy));
    });

    private static Task PrimaryActionAsync(HttpContext context, AttendanceSessionDemoBridgeService bridge) => Handle(context, async _ =>
    {
        var message = await bridge.ViewNextDemoRosterAsync(context.RequestAborted);
        return Results.Ok(new { message });
    });

    private static Task FlagExceptionAsync(HttpContext context, AttendanceSessionDemoBridgeService bridge) => Handle(context, async _ =>
    {
        var message = await bridge.FlagNextDemoAdjustmentAsync(context.RequestAborted);
        return Results.Ok(new { message });
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, SessionJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw SessionError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw SessionError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (SessionError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
