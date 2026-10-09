namespace Biolog.Features.OperationalDashboard.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class DashboardError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    /// <summary>ADR-0002 Decision 2: "there is no default tenant" - a request whose host channel resolved no tenant is refused, never silently defaulted.</summary>
    public static DashboardError TenantContextMissing() => new(
        "no tenant context is resolved for this request", "tenant_context_missing", 400);

    /// <summary>BIO-RPT-004-F01: the requesting actor must be named so AC01's permission gate has something to check - no authentication story has landed yet (BIO-INT-001 is W12), the same disclosed seam every sibling story uses.</summary>
    public static DashboardError ActorRequired() => new(
        "X-Actor-Id is required to resolve who is asking - BIO-RPT-004-AC01's dashboard.view_operational_health permission gate resolves against it", "actor_required", 400);

    /// <summary>BIO-RPT-004-F01: tenant scoping alone is not authorization - the requesting actor must hold 'dashboard.view_operational_health'.</summary>
    public static DashboardError Forbidden() => new(
        "the requesting actor does not hold the 'dashboard.view_operational_health' permission", "dashboard_view_forbidden", 403);

    /// <summary>BIO-RPT-004-F03 (Critical): a real permission grant does not make a caller-asserted actor id trustworthy - a cryptographic provenance proof is required BEFORE the permission check even runs.</summary>
    public static DashboardError ActorProvenanceRequired() => new(
        "a verified X-Actor-Provenance proof for the asserted actor is required to view operational health (ADR-0006 Decision 3)", "actor_provenance_required", 401);
}
