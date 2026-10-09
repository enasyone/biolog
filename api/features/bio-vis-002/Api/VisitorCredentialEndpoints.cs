using System.Text.Json;
using Biolog.Features.VisitorCredentials.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.VisitorCredentials.Api;

/// <summary>
/// <c>/api/v1/visitors/*</c> and <c>/api/v1/visitor-credentials/*</c> (ADR-0004 S1.1 versioning),
/// tenant-plane.
///
/// <b>Takes over <c>POST /api/v1/visitors/{visitorId}/check-out</c> from BIO-VIS-001's own
/// (narrowed) <c>VisitorEndpoints.cs</c></b> - see this class's own <see cref="CheckOutAsync"/>
/// and <c>docs/features/bio-vis-002/README.md</c> "Why this story edits BIO-VIS-001's own
/// endpoint mapping" for the full disclosed-handoff record. Every other BIO-VIS-001 route
/// (preregister/read/approve/deny/check-in/evidence-capture/config/the three headerless demo
/// bridge routes) is untouched.
///
/// Read/checkout-orchestration require an asserted actor (audit attribution) but no provenance
/// proof and no permission check - ADR-0006 Decision 2. <see cref="GrantAsync"/>/
/// <see cref="RevokeAsync"/>/<see cref="ReconcileAsync"/> are this feature's own authorization
/// decisions and each require both a permission grant and a verified ADR-0006 Decision 3
/// provenance proof.
///
/// <b><c>POST /api/v1/visitor-credentials/reconcile</c> (BIO-VIS-002-F06, Seyi round-5 FAIL)</b> -
/// the guaranteed-availability operator-recovery trigger for a stranded card, independent of any
/// grant/checkout/revoke ever happening again for the affected person. See
/// <see cref="ReconcileAsync"/> and <c>VisitorCredentialService.ReconcileStrandedCardsForPersonAsync</c>'s
/// own header for the full record.
/// </summary>
public static class VisitorCredentialEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var visitors = endpoints.MapGroup("/api/v1/visitors");
        visitors.MapPost("/{visitorId}/credential", GrantAsync);
        visitors.MapGet("/{visitorId}/credential-history", GetHistoryAsync);

        // AC03 - this story's own orchestrating checkout, taking over BIO-VIS-001's own route.
        visitors.MapPost("/{visitorId}/check-out", CheckOutAsync);

        var credentials = endpoints.MapGroup("/api/v1/visitor-credentials");
        credentials.MapPost("/{credentialId}/revoke", RevokeAsync);

        // BIO-VIS-002-F06 (Seyi round-5 FAIL) - the guaranteed-availability operator-recovery
        // trigger: does not depend on a grant/checkout/revoke ever happening again for this
        // person. Gated identically to GrantAsync/RevokeAsync - this is a privileged operator
        // action against physical access, not a read.
        credentials.MapPost("/reconcile", ReconcileAsync);
    }

    private static Task GrantAsync(HttpContext context, VisitorCredentialService service, ActorProvenanceGate provenanceGate, string visitorId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<GrantVisitorCredentialRequest>(context);
        var credential = await service.GrantAsync(visitorId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VisitorCredentialResponse.From(credential));
    });

    private static Task RevokeAsync(HttpContext context, VisitorCredentialService service, ActorProvenanceGate provenanceGate, string credentialId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<RevokeVisitorCredentialRequest>(context);
        var credential = await service.RevokeAsync(credentialId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(VisitorCredentialResponse.From(credential));
    });

    private static Task ReconcileAsync(HttpContext context, VisitorCredentialService service, ActorProvenanceGate provenanceGate) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<ReconcileStrandedCardsRequest>(context);
        if (string.IsNullOrWhiteSpace(request.PersonId))
        {
            throw VisitorCredentialError.Validation([new ValidationErrorItem("person_id", "required", "person_id is required")]);
        }

        var result = await service.ReconcileStrandedCardsForPersonAsync(request.PersonId, actor, correlationId, context.RequestAborted);
        return Results.Ok(new ReconcileStrandedCardsResponse
        {
            PersonId = request.PersonId,
            ResolvedSequenceNumbers = result.Resolved,
            UnresolvedSequenceNumbers = result.StillUnresolved,
        });
    });

    private static Task CheckOutAsync(HttpContext context, VisitorCredentialService service, string visitorId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedCheckOutRequest>(context);
        var (visitor, revoked) = await service.CheckOutAsync(visitorId, request.Version, actor, correlationId, context.RequestAborted);
        var now = Identifiers.UtcInstant.Now();
        return Results.Ok(new VisitorCheckOutResponse
        {
            VisitorId = visitor.Id,
            TenantId = visitor.TenantId,
            VisitStatus = visitor.VisitStatusWire,
            EffectiveStatus = VisitorOverstayEvaluator.EffectiveStatus(visitor, now).ToWireString(),
            CheckedOutAt = visitor.CheckedOutAt,
            RevokedCredential = revoked is null ? null : VisitorCredentialResponse.From(revoked),
        });
    });

    private static Task GetHistoryAsync(HttpContext context, VisitorCredentialService service, string visitorId) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var (visitor, credentials) = await service.GetHistoryAsync(visitorId, context.RequestAborted);
        var now = Identifiers.UtcInstant.Now();
        return Results.Ok(new VisitorCredentialHistoryResponse
        {
            VisitorId = visitor.Id,
            FullName = visitor.FullName,
            HostApprovalStatus = visitor.HostApprovalStatusWire,
            VisitStatus = visitor.VisitStatusWire,
            EffectiveStatus = VisitorOverstayEvaluator.EffectiveStatus(visitor, now).ToWireString(),
            CreatedAt = visitor.CreatedAt,
            CheckedInAt = visitor.CheckedInAt,
            CheckedOutAt = visitor.CheckedOutAt,
            ExpiresAt = visitor.ExpiresAt,
            Credentials = [.. credentials.Select(VisitorCredentialResponse.From)],
        });
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, VisitorCredentialJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw VisitorCredentialError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw VisitorCredentialError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (VisitorCredentialError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
