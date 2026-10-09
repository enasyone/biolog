namespace Biolog.Features.MobileVerification.Domain;

/// <summary>
/// BIO-MOB-004-AC03 ("Actions obey role permissions"). Mobile has no human officer/actor
/// identity today (only device-level auth - see <c>mobile/lib/core/sync/device_credential_store.dart</c>'s
/// own disclosed-gap comment, and ADR-0006, which never mentions a device principal at all), so
/// this feature cannot gate the officer's own on-device actions by a human role the way
/// BIO-MOB-005-AC04 gated its (backend-only, human-facing) duplicate-review action. Flagged and
/// escalated (hive thread <c>conv-bio-mob-004-ac03</c>, 2026-09-30) before building this - no
/// existing ADR or precedent covers it.
///
/// This feature instead attaches the role to the already-authenticated DEVICE, not to an
/// individual human (plausible for a fixed-post/kiosk verification fleet - <c>DeviceRegistry</c>'s
/// own <c>Site</c>/<c>Network</c> fields already assume something like this). A closed,
/// two-value role determines which mobile-facing actions the device's own requests may perform:
/// <see cref="Officer"/> (the default - execute a verification request, read a person's
/// verification history) and <see cref="Supervisor"/> (adds overriding a computed deny, always
/// with a mandatory reason code, mirroring BIO-MOB-005's own <c>DecisionReasonRequired</c>
/// pattern). The ASSIGNMENT of a device's role is a real, human-actor-RBAC-gated write (ADR-0006
/// Decision 3 + BIO-IDN-006's <c>Rbac</c> module, mirroring BIO-MOB-005's own
/// <c>ManagePolicyPermissionKey</c> pattern exactly) - only the role CHECK on the device's own
/// verification requests is novel.
///
/// Approved by Kemi (Chief Architect, 2026-09-30, hive thread <c>conv-bio-mob-004-ac03</c>,
/// formalized as an extension to ADR-0006's actor-identity/authorization-boundary scope for the
/// device-authenticated case): an authenticated device identity (X-Biolog-Device-Id/Secret,
/// hashed + constant-time-compared) is a genuine principal-channel realization, not a bare
/// asserted claim like <c>X-Actor-Id</c> - gating an authorization decision on it does not repeat
/// ADR-0006's own anti-pattern, it sidesteps it. Per that ruling's ownership-boundary condition,
/// the deviceId-&gt;role mapping lives entirely in this story's own owned path
/// (<c>api/features/bio-mob-004/**</c>), referencing the device id as an opaque foreign key -
/// <c>DeviceRegistry</c>'s own <c>Device</c> record is never touched.
/// </summary>
public enum VerificationDeviceRole
{
    Officer,
    Supervisor,
}

public static class VerificationDeviceRoleExtensions
{
    public static string ToWireString(this VerificationDeviceRole role) => role switch
    {
        VerificationDeviceRole.Officer => "OFFICER",
        VerificationDeviceRole.Supervisor => "SUPERVISOR",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out VerificationDeviceRole role)
    {
        switch (value)
        {
            case "OFFICER": role = VerificationDeviceRole.Officer; return true;
            case "SUPERVISOR": role = VerificationDeviceRole.Supervisor; return true;
            default: role = default; return false;
        }
    }

    /// <summary>The closed set of mobile-facing verification actions gated by AC03. Both roles may execute a verification request and read a person's verification history; only <see cref="VerificationDeviceRole.Supervisor"/> may override a computed deny.</summary>
    public static bool Permits(this VerificationDeviceRole role, VerificationAction action) => action switch
    {
        VerificationAction.ExecuteDecision => true,
        VerificationAction.ReadHistory => true,
        VerificationAction.OverrideDeny => role == VerificationDeviceRole.Supervisor,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "unreachable: exhaustive switch"),
    };
}

public enum VerificationAction
{
    ExecuteDecision,
    ReadHistory,
    OverrideDeny,
}

public static class VerificationActionExtensions
{
    public static string ToWireString(this VerificationAction action) => action switch
    {
        VerificationAction.ExecuteDecision => "execute_decision",
        VerificationAction.ReadHistory => "read_history",
        VerificationAction.OverrideDeny => "override_deny",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "unreachable: exhaustive switch"),
    };
}

/// <summary>Per-tenant, per-device role assignment. Conservative default (<see cref="VerificationDeviceRole.Officer"/>) for any device with no explicit assignment on record - mirrors BIO-MOB-005-AC01's own "the story's own default (no ADR/spec value exists) is conservative" precedent.</summary>
public interface IVerificationDeviceRoleRepository
{
    Task<VerificationDeviceRole> GetAsync(string tenantId, string deviceId, CancellationToken cancellationToken = default);

    Task SetAsync(string tenantId, string deviceId, VerificationDeviceRole role, CancellationToken cancellationToken = default);
}

public sealed class InMemoryVerificationDeviceRoleRepository : IVerificationDeviceRoleRepository
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string TenantId, string DeviceId), VerificationDeviceRole> _roles = new();

    public Task<VerificationDeviceRole> GetAsync(string tenantId, string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_roles.TryGetValue((tenantId, deviceId), out var role) ? role : VerificationDeviceRole.Officer);

    public Task SetAsync(string tenantId, string deviceId, VerificationDeviceRole role, CancellationToken cancellationToken = default)
    {
        _roles[(tenantId, deviceId)] = role;
        return Task.CompletedTask;
    }
}
