using System.Collections.ObjectModel;

namespace Biolog.Api.Tenancy;

/// <summary>
/// A resolved tenant context. Field names loosely mirror
/// <c>contracts/schemas/tenant-context.schema.json</c> (ADR-0002) so a future story that
/// ports real resolution to .NET does not need to rename anything downstream - this record
/// is still NOT a full implementation of that contract: <see cref="TenantContextMiddleware"/>
/// (BIO-TEN-002) constructs one from ADR-0002 Decision 1.3's <c>host</c> channel only, so it
/// carries no cross-channel agreement (Decision 1.2 - only one channel exists to agree with
/// today), no audit semantics (Decision 5, scoped to the full assembled resolver a later
/// story builds). <see cref="BindingKey"/> is honestly inert: Decision 4 (database routing)
/// has no .NET implementation yet, so <see cref="TenantContextMiddleware"/> sets it to
/// <see cref="string.Empty"/>, and nothing in this shell reads it to route a repository.
///
/// <b>Construction is internal (BIO-TEN-002-QA-03, Seyi round 3).</b> Before this, the public
/// record constructor let any downstream code holding <see cref="ITenantContextAccessor"/>
/// construct an arbitrary <c>TenantContext</c> and swap it into the accessor mid-request -
/// exactly the "provably the product of resolution" gap ADR-0002 Decision 4.1 warns about
/// ("a hand-built object... is otherwise... refused"). Only code inside this assembly can
/// call this constructor now; <c>InternalsVisibleTo</c> in <c>Biolog.Api.csproj</c> grants the
/// same access to <c>Biolog.UnitTests</c>/<c>Biolog.IntegrationTests</c> as an explicit,
/// auditable test seam - not a backdoor, since it is visible in the csproj, not a reflection
/// trick. <see cref="TenantId"/> is validated at construction: null, empty or a non-<c>ten_</c>-
/// prefixed value throws. This is deliberately NOT full canonical UUIDv7 shape validation
/// (<c>CanonicalId.IsTenantId</c> in each feature project's own <c>Identifiers/</c> folder) -
/// that strict check belongs at the one place a tenant id is actually minted or accepted from
/// a caller (BIO-TEN-001's <c>RegisterAsync</c>, BIO-TEN-002's own tenant_id shape check), and
/// enforcing it again here would break the large existing BIO-DEV-001 test suite's informal
/// fixture ids (<c>ten_asu</c>, <c>ten_grace</c>) for no additional security benefit: once
/// construction is assembly-internal, the real attacker-forgery path is already closed by
/// visibility, not by string shape. This check is defense-in-depth against an internal bug
/// (e.g. passing the wrong variable) mistaking a different identifier type for a tenant id,
/// not a re-implementation of the canonical registry.
/// <see cref="Channels"/> is copied into a <see cref="ReadOnlyCollection{T}"/> wrapping a
/// fresh list, not merely exposed as <see cref="IReadOnlyList{T}"/> over whatever the caller
/// passed in - the caller's own list could otherwise be mutated after construction (changing
/// this context's contents without ever going through the accessor), and a
/// <see cref="ReadOnlyCollection{T}"/> throws on every mutating call rather than allowing a
/// downcast back to something mutable the way an array exposed as
/// <see cref="IReadOnlyList{T}"/> would.
/// </summary>
public sealed record TenantContext
{
    public string TenantId { get; }
    public string BindingKey { get; }
    public IReadOnlyList<string> Channels { get; }

    internal TenantContext(string tenantId, string bindingKey, IReadOnlyList<string> channels)
    {
        if (!IsWellFormedTenantId(tenantId))
        {
            throw new ArgumentException(
                $"'{tenantId}' is not a well-formed tenant id (must be non-empty and 'ten_'-prefixed, ADR-0001 Decision 1)",
                nameof(tenantId));
        }

        TenantId = tenantId;
        BindingKey = bindingKey;
        Channels = new ReadOnlyCollection<string>(new List<string>(channels));
    }

    private static bool IsWellFormedTenantId(string? value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith("ten_", StringComparison.Ordinal) && value.Length > "ten_".Length;
}
