using System.Text.Json;
using System.Text.Json.Serialization;
using Biolog.Api.Audit;
using Biolog.Api.Identifiers;

namespace Biolog.Api.Tenancy;

/// <summary>
/// OPS002-F02 fix (BIO-TEN-002, 2026-10-07): BIO-OPS-002's acceptance review found that
/// switching the demo tenant/role selector (<c>web/context/session-actions.ts</c>'s
/// <c>startDemoSession</c>) revokes the old session token and issues a new one but emits no
/// durable, actor-attributed audit record at all - no tenant/role context-change audit
/// endpoint or sink existed anywhere, so AC04 had nothing to accept.
///
/// <b>SY-OPS002-06 (Kemi's ruling, round 3): reuses the SHARED <see cref="Biolog.Api.Audit.AuditEvent"/>/
/// <see cref="IAuditSink"/>/<see cref="InMemoryAuditSink"/>/<see cref="AuditActor"/>/
/// <see cref="AuditTarget"/> directly, not a bespoke per-feature envelope/sink.</b> Rounds 1-2
/// built dedicated <c>DemoSessionContextAuditEvent</c>/<c>IDemoSessionContextAuditSink</c>
/// types, following every feature-project's own established convention - but this code lives
/// at the SAME host level <see cref="Biolog.Api.Audit.AuditEvent"/> already does (not a
/// feature project, for the same reason <see cref="DemoSessionTenantResolver"/> doesn't),
/// carries no classified value (only non-sensitive tenant/role/actor labels), and needed no
/// projection/redaction machinery a bespoke type would have genuinely required elsewhere - a
/// second, narrower envelope shape here would have been exactly the drift that type's own
/// header warns against. <c>previous_tenant_id</c>/<c>previous_role_id</c>/
/// <c>previous_actor_id</c>/<c>new_role_id</c> go in <see cref="AuditEvent.Detail"/> (the same
/// "internal technical metadata" allowlist convention <c>PhotoQualityAuditEventBuilder</c>/
/// <c>PhotoAuditEventBuilder</c> already use on their OWN bespoke event types, applied here to
/// the shared one for the first time - see that type's own updated header). <c>new_tenant_id</c>
/// is the event's own required <see cref="AuditEvent.TenantId"/> instead, since it genuinely
/// IS this event's tenant, not merely incidental detail.
///
/// <b>DI registration note</b>: registers its OWN <see cref="InMemoryAuditSink"/> instance by
/// its CONCRETE type (<c>services.AddSingleton&lt;InMemoryAuditSink&gt;()</c> in
/// <see cref="Biolog.Api.Modules.TenantModule"/>), never through the shared
/// <see cref="IAuditSink"/> INTERFACE slot - <see cref="Biolog.Api.Modules.DeviceCertificationModule"/>'s
/// own header already discloses why a second <c>AddSingleton&lt;IAuditSink, ...&gt;()</c>
/// would silently shadow <see cref="Biolog.Api.Modules.DeviceRegistryModule"/>'s own
/// registration (ASP.NET Core resolves one interface slot to whichever module registered it
/// LAST) rather than adding an independent second sink; registering the CONCRETE type instead
/// sidesteps that exact, already-documented collision while still reusing the real shared
/// type, not a copy of it.
///
/// <b>Why this lives in <c>Biolog.Api.Tenancy</c>, not a feature project.</b> Same reason
/// <see cref="DemoSessionTenantResolver"/> does (see that class's own header): this is about
/// the demo-session bridge mechanism itself, which that resolver already owns at this exact
/// layer, and the two are tightly coupled (this endpoint's whole authorization model IS that
/// resolver's own verification, see below) - splitting them across a host file and a separate
/// feature project would be an artificial seam.
///
/// <b>Authorization: the demo session token itself, not a second, separate
/// <c>ActorProvenanceGate</c>.</b> Every other mutating endpoint in this codebase needs BOTH
/// an <c>ActorProvenanceGate</c> proof AND a permission-gateway check (ADR-0006 Decision 3) -
/// re-derived from first principles here, not assumed, per the lesson BIO-INT-005's own
/// ZB-INT005-01/02 remediation learned the hard way (re-justify every route's OWN actual
/// requirement, never inherit one by shape). This endpoint has neither, deliberately:
/// <list type="bullet">
/// <item>No separate provenance gate, because <see cref="DemoSessionTenantResolver.TryVerify"/>
/// already IS the provenance proof for this exact mechanism - the same HMAC-signed
/// <c>X-Web-Session-Token</c> <see cref="TenantContextMiddleware"/> already treats as
/// authoritative for resolving tenant context. Layering a second, independent provenance
/// secret on top of the one that already cryptographically binds this request's actor/tenant/
/// role together would not add any real assurance, only a second secret to manage.</item>
/// <item>No permission check, because there is no authorization DECISION here to gate - the
/// entire TD-WEB-PROVENANCE-BRIDGE scope (Kemi's own ruling) is that self-selecting any demo
/// role is already the approved, intentional behavior; this endpoint only records that a
/// self-selection happened, exactly like the self-selection itself needs no permission. A
/// "permission to record you switched" concept does not exist anywhere else in this codebase
/// (an audit write is always a side effect of a permitted action, never its own separately
/// gated action) and inventing one here would be authorization theatre, not a real boundary.</item>
/// </list>
///
/// <b>ZB-OPS002-01 (Medium, round 4): <c>previous_actor_id</c> is DERIVED from
/// <c>previous_role_id</c>, never trusted verbatim from the caller.</b> A round-3 oversight -
/// <c>previous_tenant_id</c>/<c>previous_role_id</c> were already constrained to the closed
/// "alpha"/"beta" and known-role allowlists above, but <c>previous_actor_id</c> was copied
/// straight from the request body into <see cref="AuditEvent.Detail"/> at classification
/// "internal" with no validation at all - any holder of a genuinely valid synthetic demo
/// session could have persisted arbitrary caller-controlled text (personal data, a secret, an
/// oversized string) there, exactly what Kemi's canonical-id/non-secret allowlist ruling (the
/// same ruling this file's own <c>ActorIdByRole</c> allowlist already enforces for the OTHER
/// two fields) was meant to prevent across the board, not just for two of three fields. Fixed:
/// <c>previous_actor_id</c> must equal <c>ActorIdByRole[previous_role_id]</c> or the request is
/// rejected (400 <c>previous_actor_id_role_mismatch</c>); the value actually written to
/// <see cref="AuditEvent.Detail"/> is the DERIVED one, not the caller's own string, so even a
/// byte-for-byte-matching-but-coincidental caller value can never end up stored by construction
/// - only a value this endpoint itself computed ever lands in the record.
///
/// <b>Disclosed trust limitation, carried over from round 1 (SY-OPS002-05, accepted by Kemi
/// as a narrow, disclosed residual under ADR-0003 Decision 7 - no further code change here):
/// the "previous" tenant/role/actor fields are self-reported, not independently re-verified
/// against the actual OLD token.</b> The caller (the Next.js server action) already verified
/// the OLD token's own signature itself, moments earlier, before revoking it - this endpoint
/// trusts that report rather than demanding the (by now revoked, unverifiable) old token too.
/// ZB-OPS002-01's fix narrows this gap (previous_actor_id can no longer disagree with
/// previous_role_id, closing the free-text injection risk) but does not close it entirely -
/// a caller could still misreport which role/tenant was PREVIOUSLY active (just not an
/// internally-inconsistent or arbitrary one). Acceptable because nothing downstream makes an
/// authorization decision based on the "previous" fields - they exist purely for an operator
/// reading the audit trail, not as an input to any later
/// check.
///
/// <b>Correlation id</b>: validated against the canonical <c>evt_</c> shape
/// (<see cref="CanonicalId.IsWellFormed(string?, string)"/>) exactly like
/// <c>DeviceService</c>/<c>DeviceCertificationService</c> already do for their own
/// <see cref="AuditEvent.CorrelationId"/> - an arbitrary caller-supplied
/// <c>X-Correlation-Id</c> string is discarded and a fresh <c>evt_</c> is minted instead of
/// passed through, never trusted as-is.
/// </summary>
public static class DemoSessionContextAuditEndpoints
{
    public const string TokenHeaderName = "X-Web-Session-Token";

    private static readonly IReadOnlyDictionary<string, string> ActorIdByRole = new Dictionary<string, string>
    {
        // Mirrors `web/lib/demo-identity.ts`'s own `DEMO_PERSON_ID_BY_ROLE` and
        // `DemoSessionIdentitySeed.cs`'s own `Identities` list - duplicated across the
        // stack/assembly boundary by necessity, the same way `DEMO_ROLE_IDS`/`DEMO_TENANT_IDS`
        // already are between `demo-identity.ts` and `session-context.tsx`.
        ["admin"] = "per_01a113f6-6268-730c-a5ce-44d97f0aa7f5",
        ["staff"] = "per_01a113f6-6268-7c44-a9e5-3aee6ff59416",
        ["viewer"] = "per_01a113f6-6268-757e-926c-4a1b5f98b45b",
    };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/tenancy/demo-session/context-change", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        DemoSessionTenantResolver resolver,
        InMemoryAuditSink auditSink)
    {
        var presentedToken = context.Request.Headers[TokenHeaderName].ToString();
        var verification = resolver.TryVerify(string.IsNullOrEmpty(presentedToken) ? null : presentedToken);
        if (verification is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "demo_session_invalid",
                detail: "No valid, unexpired demo session token was presented.");
        }

        ContextChangeRequestBody? body;
        try
        {
            body = await context.Request.ReadFromJsonAsync<ContextChangeRequestBody>(context.RequestAborted);
        }
        catch (JsonException)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "malformed_request_body");
        }

        body ??= new ContextChangeRequestBody(null, null, null);
        var previousFields = new[] { body.PreviousTenantId, body.PreviousRoleId, body.PreviousActorId };
        if (previousFields.Any(v => v is not null) && previousFields.Any(v => v is null))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "incomplete_previous_context");
        }

        if (body.PreviousTenantId is not null && body.PreviousTenantId is not ("alpha" or "beta"))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "invalid_previous_tenant_id");
        }

        string? derivedPreviousActorId = null;
        if (body.PreviousRoleId is not null)
        {
            if (!ActorIdByRole.TryGetValue(body.PreviousRoleId, out derivedPreviousActorId))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "invalid_previous_role_id");
            }

            // ZB-OPS002-01 (Medium): previous_actor_id must be DERIVED from previous_role_id,
            // never trusted verbatim from the caller - a valid synthetic demo session could
            // otherwise persist arbitrary caller-controlled text (personal data, a secret, an
            // oversized string) into Detail at classification "internal", exactly what Kemi's
            // canonical-id/non-secret allowlist ruling was meant to prevent. Reject a mismatch
            // outright rather than silently substituting the derived value, so a caller passing
            // the wrong actor id for the role it claims gets a clear 400, not a quietly
            // "corrected" audit record.
            if (body.PreviousActorId != derivedPreviousActorId)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "previous_actor_id_role_mismatch");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var newTenantId = verification.Value.Context.TenantId;
        var newRoleId = verification.Value.RoleId;
        var newActorId = ActorIdByRole.GetValueOrDefault(newRoleId, newRoleId);
        var suppliedCorrelationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();

        var auditEvent = new AuditEvent
        {
            EventId = CanonicalId.NewEventId(),
            SchemaVersion = 1,
            TenantId = newTenantId,
            Actor = new AuditActor("user", newActorId),
            Device = null,
            Action = "tenancy.demo_session.context_changed",
            Target = new AuditTarget("demo_session_context", newActorId),
            Outcome = AuditOutcome.Allowed,
            ReasonCode = "context_switch",
            CorrelationId = CanonicalId.IsWellFormed(suppliedCorrelationId, "evt") ? suppliedCorrelationId! : CanonicalId.NewEventId(),
            CausationId = null,
            OccurredAt = now,
            ReceivedAt = now,
            Classification = "internal",
            Detail = new Dictionary<string, object?>
            {
                ["previous_tenant_id"] = body.PreviousTenantId,
                ["previous_role_id"] = body.PreviousRoleId,
                // Derived from previous_role_id (validated above), never the caller's own
                // previous_actor_id string verbatim - ZB-OPS002-01.
                ["previous_actor_id"] = derivedPreviousActorId,
                ["new_role_id"] = newRoleId,
            },
        };

        try
        {
            await AuditAsync(auditSink, auditEvent, context.RequestAborted);
        }
        catch (AuditUnavailableException ex)
        {
            // Mirrors DeviceRegistryModule's own identical catch for the same exception -
            // established convention, not invented here.
            return Results.Problem(title: "audit_unavailable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new
        {
            data = new
            {
                event_id = auditEvent.EventId,
                new_tenant_id = newTenantId,
                new_role_id = newRoleId,
                new_actor_id = newActorId,
            },
        });
    }

    /// <summary>ADR-0003 Decision 7's audit-atomicity pattern, mirrored from
    /// <c>DeviceService.AuditAsync</c> - a <see cref="IAuditSink.RecordAsync"/> that throws is
    /// ambiguous (a real store can durably commit and still throw on acknowledgement), so this
    /// verifies via <see cref="IAuditSink.ExistsAsync"/> before concluding the write genuinely
    /// failed. SY-OPS002-05 (Kemi's ruling, accepted as a disclosed residual, no code change
    /// needed): unlike <c>DeviceService</c>, this method has no per-aggregate lock to hold
    /// across the audit-then-commit sequence, since there is no aggregate here to lock - the
    /// resolved tenant/actor pair is read-only, verified fresh from the request's own token on
    /// every call, never a stored record this endpoint itself mutates.</summary>
    private static async Task AuditAsync(InMemoryAuditSink auditSink, AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try
        {
            await auditSink.RecordAsync(auditEvent, cancellationToken);
            return;
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new AuditUnavailableException(
                "the audit sink failed and could not be verified; the context change was not recorded", ex);
        }

        if (!exists)
        {
            throw new AuditUnavailableException(
                "the audit sink failed and no matching event was found; the context change was not recorded");
        }
    }

    private sealed record ContextChangeRequestBody(
        [property: JsonPropertyName("previous_tenant_id")] string? PreviousTenantId,
        [property: JsonPropertyName("previous_role_id")] string? PreviousRoleId,
        [property: JsonPropertyName("previous_actor_id")] string? PreviousActorId);
}
