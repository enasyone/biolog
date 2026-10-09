namespace Biolog.Features.PersonRegistry.Tenancy;

/// <summary>
/// This feature's own minimal seam onto ADR-0002's resolved tenant context - deliberately
/// NOT a reference to <c>Biolog.Api.Tenancy.ITenantContextAccessor</c>/<c>TenantContext</c>,
/// because this project has no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c> (one-way
/// dependency, <c>scripts/check-module-project-references.sh</c> - see this project's own
/// csproj remarks).
///
/// PERSON is tenant-plane (ADR-0001's identifier registry: <c>per</c>, plane <c>tenant</c>),
/// so AC02 ("tenant boundary is mandatory") requires this feature to read the REAL resolved
/// tenant, never a caller-supplied value - exactly the rule <c>DeviceService</c>
/// (BIO-DEV-001) already applies against <c>ITenantContextAccessor</c> directly, from inside
/// <c>Biolog.Api</c> where that type lives. This feature cannot reach that type directly, so
/// it defines this narrower interface instead and lets whatever registers this feature into
/// the packaged host (<c>BioIdn001Module</c>, <c>api/Biolog.Api/Modules/</c>) provide an
/// adapter that reads the real accessor and implements this shape - the same "feature defines
/// the abstraction it needs; the host wires a concrete implementation" seam
/// <c>ITenantDomainRegistry</c>/<c>ITenantDomainAuditSink</c> already use for storage, applied
/// here to tenant context instead.
///
/// Deliberately carries only <see cref="TenantId"/>, not a full <c>TenantContext</c>-shaped
/// record: this feature has no use yet for <c>BindingKey</c> (Decision 4, no .NET database
/// routing exists) or <c>Channels</c> (Decision 1.2's cross-channel agreement, not assembled
/// by any story yet) - adding fields this feature never reads would be speculative surface,
/// not a real need. A later story that needs binding_key or channels extends this interface
/// when it actually has a use for them, not before.
///
/// <see cref="TenantId"/> is <c>null</c> whenever no channel resolved a tenant for the
/// request (ADR-0002 Decision 2: no implicit or default tenant, ever) - <see cref="PersonService"/>
/// fails closed on <c>null</c> exactly like <c>DeviceService</c> does against the real
/// accessor.
/// </summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
