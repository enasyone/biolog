namespace Biolog.Api.Tenancy;

/// <summary>
/// ADR-0002 Decision 3: "Mutating a resolved context throws <c>tenant_context_immutable</c>."
/// Thrown by <see cref="TenantContextAccessor.SetResolved"/> if resolution is attempted a
/// second time on the same scoped accessor - the accessor is set-once, not "last write wins":
/// no setter, no swap, no re-resolution mid-request (BIO-TEN-002-QA-03, Seyi round 3). Should
/// never happen in production - <see cref="TenantContextMiddleware"/> runs exactly once per
/// request - but if a scoped accessor's <see cref="TenantContextAccessor.Context"/> was
/// already set before the middleware runs (a bug, or a tampering attempt via some other
/// component reaching the concrete accessor type), the middleware's own attempt to set the
/// real resolved context must fail loudly rather than silently overwrite whatever was already
/// there - silently overwriting is exactly the "swap" Decision 3 forbids.
/// </summary>
public sealed class TenantContextImmutableException() : Exception(
    "tenant_context_immutable: a tenant context was already resolved for this request");
