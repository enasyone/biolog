namespace Biolog.Features.ApiSecurity;

/// <summary>
/// BIO-INT-003-AC02: this project's own closed copy of BIO-IDN-006's role catalogue, used as
/// the OAuth2 scope vocabulary for service accounts - a scope IS a role wire string, so a
/// granted scope maps directly onto BIOLOG's existing permission model rather than inventing a
/// second, parallel taxonomy. Documented, deliberate duplication (a third independent copy -
/// see BIO-INT-001's own <c>KnownRoles.cs</c> and this project's csproj comment for why this
/// project cannot reference either bio-idn-006 or bio-int-001 to share one copy instead), not
/// an accidental drift risk: if BIO-IDN-006's role catalogue ever changes, every duplicate
/// (this one, BIO-INT-001's) needs the same change, tracked the same way BIO-DEV-003's
/// <c>DeviceDescriptor</c> duplication already is.
///
/// <b>Security-review fix (Zainab, ZB-INT003-07, High - round 4): <c>SuperAdmin</c> is
/// deliberately EXCLUDED from this catalogue - it is NOT an issuable service-account scope.</b>
/// BIO-IDN-006's own 11-role catalogue includes it for HUMAN role assignment (a real person,
/// under that story's own tightly-gated <c>RoleAssignmentService</c>); this project does not
/// change that. But round 3's <c>RolePermissionGatewayAdapter</c> mirrored
/// <c>RoleType.SuperAdmin</c>'s "never needs a grant" domain invariant for service-account
/// tokens too, and Zainab's round-4 review correctly found that unsafe: an actor holding only
/// the narrow <see cref="ServiceAccountService.ManagePermission"/> could mint a service-account
/// credential scoped <c>SuperAdmin</c>, which would then auto-pass EVERY current and future
/// domain-permission check - a genuine privilege-escalation primitive, not merely a stricter
/// human-role mirror. The API architecture's own rule is explicit: service accounts/OAuth scopes
/// map to domain permissions, never a blanket tenant-admin token. The fix is to make
/// <c>SuperAdmin</c> un-issuable at the root (this catalogue), not to try to constrain what a
/// SuperAdmin-scoped token could do after the fact - <see cref="IsKnown"/> rejects it the same
/// way it already rejects any string outside this closed set, and
/// <c>RolePermissionGatewayAdapter</c>'s own former short-circuit for it was removed entirely
/// (unreachable once creation-time validation holds, but removed rather than left as dead,
/// misleading code implying this adapter still honors that case).
/// </summary>
public static class KnownRoles
{
    public static readonly IReadOnlySet<string> WireFormats = new HashSet<string>(StringComparer.Ordinal)
    {
        "TenantAdmin", "RegistrarHr", "Enrolment", "Biometric", "CardProduction",
        "Security", "Gate", "Auditor", "Helpdesk", "ReadOnly",
    };

    public static bool IsKnown(string scope) => WireFormats.Contains(scope);
}
