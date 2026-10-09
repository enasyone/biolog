using Biolog.Features.TenantDomainRegistry;
using Biolog.Features.TenantDomainRegistry.Domain;

namespace Biolog.Api.Tenancy;

/// <summary>
/// The tenant middleware hook (BIO-FND-008-AC04). Runs on every request ahead of endpoint
/// execution and gives downstream code a single, consistent place to read a resolved
/// tenant from <see cref="ITenantContextAccessor"/>.
///
/// Implements ADR-0002 Decision 1.3's <c>host</c> channel only (BIO-TEN-002, per ADR-0002
/// Decision 6: "BIO-TEN-002 ... implements the host channel exactly as Decision 1 specifies
/// it - a real, production resolution mechanism, not a shim"). The <c>principal</c> and
/// <c>service</c> channels (Decision 1.1) are not implemented by any story yet, so Decision
/// 1.2's cross-channel agreement check has nothing to reconcile against today - only one
/// channel can ever be present, so "every channel that supplied a tenant must name the same
/// tenant" is vacuously satisfied until a second channel ships. That reconciliation, and
/// this middleware's fail-closed <c>tenant_context_missing</c>/audit-before-deny behavior for
/// a genuine multi-channel disagreement (Decision 5), belong to the story that assembles the
/// full Decision 1 resolver from all three channels - this class resolves <c>host</c> only.
///
/// Per ADR-0002 Decision 2 ("never resolve tenancy yourself... there is no default tenant"),
/// an unrecognised or unverified host - or any exception from resolution - fails closed to
/// <c>null</c>, exactly as before this story wired in a real channel; nothing here fabricates
/// a placeholder tenant. <see cref="TenantContext.BindingKey"/> is not yet a real database
/// binding: ADR-0002 Decision 4 (database routing) has no .NET implementation yet, so this
/// middleware sets it to <see cref="string.Empty"/> - honestly inert, not a fabricated
/// binding, and nothing in this shell reads it to route a tenant repository yet. A future
/// story that ports Decision 4 replaces that placeholder without changing this middleware's
/// shape.
///
/// <b>TD-WEB-PROVENANCE-BRIDGE round 5 (ZB-WEBPROV-02)</b>: <see cref="DemoSessionTenantResolver"/>
/// is checked FIRST, before the <c>host</c> channel - see that class's own header for the full
/// architectural account. A verified demo-session marker (<c>X-Web-Session-Token</c>) confines
/// resolution to one hardcoded synthetic tenant, completely bypassing host-channel resolution
/// for that one request; anything without a verified marker resolves exactly as it did before
/// this resolver existed, untouched.
/// </summary>
public sealed class TenantContextMiddleware
{
    private const string DemoSessionTokenHeader = "X-Web-Session-Token";

    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        TenantContextAccessor accessor,
        TenantDomainService tenantDomainService,
        DemoSessionTenantResolver demoSessionResolver)
    {
        var demoSessionToken = context.Request.Headers[DemoSessionTokenHeader].ToString();
        var demoSessionContext = demoSessionResolver.TryResolve(demoSessionToken);
        accessor.SetResolved(demoSessionContext ?? await ResolveHostChannelAsync(context, tenantDomainService));
        await _next(context);
    }

    private static async Task<TenantContext?> ResolveHostChannelAsync(HttpContext context, TenantDomainService tenantDomainService)
    {
        try
        {
            var domain = await tenantDomainService.ResolveAsync(context.Request.Host.Host, context.RequestAborted);
            return new TenantContext(domain.TenantId, bindingKey: string.Empty, channels: new[] { "host" });
        }
        catch (TenantDomainError)
        {
            return null;
        }
    }
}
