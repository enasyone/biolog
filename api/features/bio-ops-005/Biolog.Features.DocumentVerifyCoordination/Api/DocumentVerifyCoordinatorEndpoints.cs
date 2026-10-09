using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.DocumentVerifyCoordination.Api;

/// <summary>
/// The 8 endpoints Kemi's BIO-OPS-005 ruling calls for (point 1), one per <c>DocumentsPort</c>
/// method, all under <c>/api/v1/documents/*</c> - the exact route family
/// <c>web/features/documents/live.ts</c>'s own <c>LIVE_ENDPOINTS</c> already targets.
///
/// <para>
/// <b>Session threading without a port redesign (point 4):</b> <c>DocumentsPort</c>'s 8 methods
/// are zero-arg and stay that way. <see cref="CaptureAsync"/>'s response sets an <c>HttpOnly</c>
/// <c>document_session_id</c> cookie; every subsequent step is same-origin, so the browser sends
/// it automatically (<c>fetch</c>'s default <c>credentials</c> already include same-origin
/// cookies) - the server resolves the session from the cookie, never from a caller-supplied body
/// field. A missing/expired/foreign-tenant cookie is <see cref="DocumentVerifyCoordinatorError.NoActiveSession"/>
/// (409), never a crash.
/// </para>
/// </summary>
public static class DocumentVerifyCoordinatorEndpoints
{
    private const string BasePath = "/api/v1/documents";
    public const string SessionCookieName = "document_session_id";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Literal full paths on every MapPost call, not `$"{BasePath}/..."` interpolation - the
        // same convention BIO-DOC-009/010's own endpoint files use, which Seyi's own acceptance
        // probe structurally greps for (`MapPost\(.*documents/$endpoint`).
        endpoints.MapPost("/api/v1/documents/capture", CaptureAsync);
        endpoints.MapPost("/api/v1/documents/mrz", MrzAsync);
        endpoints.MapPost("/api/v1/documents/barcode", BarcodeAsync);
        endpoints.MapPost("/api/v1/documents/ocr", OcrAsync);
        endpoints.MapPost("/api/v1/documents/identify", IdentifyAsync);
        endpoints.MapPost("/api/v1/documents/nfc", NfcAsync);
        endpoints.MapPost("/api/v1/documents/authenticity", AuthenticityAsync);
        endpoints.MapPost("/api/v1/documents/selfie-comparison", SelfieComparisonAsync);
    }

    private static Task CaptureAsync(HttpContext context, DocumentVerifyCoordinatorService service) => Handle(context, async correlationId =>
    {
        var actorId = ActorResolver.ResolveOrDefault(context.Request);
        var (sessionId, outcome) = await service.CaptureAsync(actorId, correlationId, context.RequestAborted);

        context.Response.Cookies.Append(SessionCookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = BasePath,
        });

        return Results.Ok(EvidenceStepResponse.From(outcome));
    });

    private static Task MrzAsync(HttpContext context, DocumentVerifyCoordinatorService service) =>
        HandleStep(context, service, static (svc, sessionId, actorId, correlationId, ct) => svc.RunMrzAsync(sessionId, actorId, correlationId, ct));

    private static Task BarcodeAsync(HttpContext context, DocumentVerifyCoordinatorService service) =>
        HandleStep(context, service, static (svc, sessionId, actorId, correlationId, ct) => svc.RunBarcodeAsync(sessionId, actorId, correlationId, ct));

    private static Task OcrAsync(HttpContext context, DocumentVerifyCoordinatorService service) =>
        HandleStep(context, service, static (svc, sessionId, actorId, correlationId, ct) => svc.RunOcrAsync(sessionId, actorId, correlationId, ct));

    private static Task IdentifyAsync(HttpContext context, DocumentVerifyCoordinatorService service) =>
        HandleStep(context, service, static (svc, sessionId, actorId, correlationId, ct) => svc.IdentifyDocumentAsync(sessionId, actorId, correlationId, ct));

    private static Task NfcAsync(HttpContext context, DocumentVerifyCoordinatorService service) =>
        HandleStep(context, service, static (svc, sessionId, actorId, correlationId, ct) => svc.RunNfcAsync(sessionId, actorId, correlationId, ct));

    private static Task AuthenticityAsync(HttpContext context, DocumentVerifyCoordinatorService service) =>
        HandleStep(context, service, static (svc, sessionId, actorId, correlationId, ct) => svc.CheckAuthenticityAsync(sessionId, actorId, correlationId, ct));

    private static Task SelfieComparisonAsync(HttpContext context, DocumentVerifyCoordinatorService service) =>
        HandleStep(context, service, static (svc, sessionId, actorId, correlationId, ct) => svc.CompareSelfieAsync(sessionId, actorId, correlationId, ct));

    private static Task HandleStep(
        HttpContext context,
        DocumentVerifyCoordinatorService service,
        Func<DocumentVerifyCoordinatorService, string, string, string, CancellationToken, Task<EvidenceStepOutcome>> step) =>
        Handle(context, async correlationId =>
        {
            var sessionId = RequireSessionCookie(context);
            var actorId = ActorResolver.ResolveOrDefault(context.Request);
            var outcome = await step(service, sessionId, actorId, correlationId, context.RequestAborted);
            return Results.Ok(EvidenceStepResponse.From(outcome));
        });

    private static string RequireSessionCookie(HttpContext context)
    {
        var sessionId = context.Request.Cookies[SessionCookieName];
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw DocumentVerifyCoordinatorError.NoActiveSession();
        }

        return sessionId;
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
        catch (DocumentVerifyCoordinatorError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
