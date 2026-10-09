namespace Biolog.Features.ParserRegistry.Model;

/// <summary>Who owns a parser definition (BIO-DOC-005-AC01/AC05): BTS-authored parsers are
/// <see cref="Global"/> (usable, read-only, by every tenant); a tenant-authored parser is
/// <see cref="Tenant"/>-scoped and visible only to that one tenant (never even revealed to exist
/// to any other tenant - see <see cref="ParserNotFoundException"/>).</summary>
public abstract record ParserOwnerScope
{
    private ParserOwnerScope()
    {
    }

    public sealed record Global : ParserOwnerScope;

    public sealed record Tenant : ParserOwnerScope
    {
        public string TenantId { get; }

        public Tenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new ArgumentException("must be a non-empty string", nameof(tenantId));
            }

            TenantId = tenantId;
        }
    }

    /// <summary>True if <paramref name="caller"/> may see/act on a record with this scope
    /// (BIO-DOC-005-AC05). BTS sees everything; a tenant sees only its own tenant-scoped records
    /// plus every global one.</summary>
    public bool IsVisibleTo(ParserCaller caller) => (this, caller) switch
    {
        (Global, _) => true,
        (Tenant, ParserCaller.Bts) => true,
        (Tenant owned, ParserCaller.Tenant callerTenant) => owned.TenantId == callerTenant.TenantId,
        _ => false,
    };
}
