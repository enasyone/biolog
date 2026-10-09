namespace Biolog.Api.Tenancy;

/// <summary>
/// Per-request, read-only view of a resolved <see cref="TenantContext"/>. Registered scoped,
/// so every service resolved for a request sees the same instance the tenant middleware hook
/// populated (BIO-FND-008-AC04). <see cref="Context"/> is <c>null</c> whenever no channel
/// resolves a tenant for the request - unconditionally before BIO-TEN-002, and now whenever
/// the request's host does not match a <c>VERIFIED</c> <c>TenantDomain</c> (ADR-0002 Decision
/// 2: no implicit or default tenant, ever).
///
/// This interface is deliberately read-only (BIO-TEN-002-QA-03, Seyi round 3): before this,
/// every consumer holding an <c>ITenantContextAccessor</c> - <c>DeviceService</c> included -
/// could also overwrite <see cref="Context"/> mid-request via the same interface's setter,
/// which is exactly the "No setter, no swap" ADR-0002 Decision 3 forbids. Only
/// <see cref="TenantContextMiddleware"/> can set a resolved context now, via the concrete
/// <see cref="TenantContextAccessor"/> type's internal <c>SetResolved</c> - see
/// <see cref="Modules.TenantModule"/> for how both the interface and the concrete type
/// resolve to the same scoped instance.
/// </summary>
public interface ITenantContextAccessor
{
    TenantContext? Context { get; }
}

public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private bool _resolved;

    public TenantContextAccessor()
    {
    }

    /// <summary>
    /// Convenience constructor for tests (and any other code) that want a fresh,
    /// already-resolved accessor without running a request pipeline. This is not the
    /// mid-request "swap" BIO-TEN-002-QA-03 closes: that was replacing an EXISTING scoped
    /// instance's <see cref="Context"/> after <see cref="TenantContextMiddleware"/> already
    /// set it, reachable by any consumer holding the shared per-request instance. A brand
    /// new instance, not yet shared with anything, carries no such risk - still goes through
    /// the same <see cref="SetResolved"/>/<see cref="TenantContext"/> validation either way.
    /// </summary>
    public TenantContextAccessor(TenantContext? context)
    {
        SetResolved(context);
    }

    public TenantContext? Context { get; private set; }

    /// <summary>
    /// The one authoritative set operation (BIO-TEN-002-QA-03). Internal: only code inside
    /// this assembly can call it - in practice only <see cref="TenantContextMiddleware"/>,
    /// which is the only production caller and the only place resolution happens.
    /// <c>InternalsVisibleTo</c> in <c>Biolog.Api.csproj</c> grants the same access to
    /// <c>Biolog.UnitTests</c>/<c>Biolog.IntegrationTests</c> as an explicit test seam. Set-once,
    /// not "last write wins": a second call - even to <c>null</c> - throws
    /// <see cref="TenantContextImmutableException"/> rather than silently overwriting, matching
    /// ADR-0002 Decision 3's "Mutating a resolved context throws <c>tenant_context_immutable</c>."
    /// </summary>
    internal void SetResolved(TenantContext? context)
    {
        if (_resolved)
        {
            throw new TenantContextImmutableException();
        }

        _resolved = true;
        Context = context;
    }
}
