using Biolog.Features.DocumentVerify.Storage;

namespace Biolog.Api.Modules;

/// <summary>
/// Registers BIO-DOC-001's provider-neutral Document Verify session/evidence store into the
/// host (BIO-FND-008-AC05 extension seam). Registered as a singleton: an in-process, in-memory
/// store has to be one instance to be useful across requests, the identical reasoning
/// <c>createInMemoryIdempotencyStore</c> documents for ADR-0004's reference store. This is
/// explicitly NOT a real tenant-scoped persistence layer - see
/// <see cref="Biolog.Features.DocumentVerify.Storage.IDocumentSessionStore"/>'s remarks and
/// <c>docs/features/bio-doc-001/DOCUMENT-VERIFY-FOUNDATION.md</c>.
///
/// <para>
/// No HTTP endpoints are mapped by this module itself. AC01-AC05 are about the session/evidence
/// MODEL - "a provider-neutral session/evidence model before MRZ, barcode, OCR and NFC engines
/// are built" - not an HTTP surface of its OWN. The "no real tenant context yet" reasoning this
/// paragraph originally gave is now stale and has been corrected: BIO-TEN-002 has since shipped
/// real host-channel tenant resolution (<see cref="Tenancy.TenantContextMiddleware"/> genuinely
/// resolves a <see cref="Tenancy.TenantContext"/> from a <c>VERIFIED</c> <c>TenantDomain</c> now),
/// and BIO-DOC-009/010/011 and BIO-OPS-005 already map real endpoints against it successfully.
/// BIO-OPS-005's own coordinator (<c>api/Biolog.Api/Modules/BioOps005Module.cs</c>) is the first
/// to map real HTTP endpoints directly onto this store, resolving the tenant itself the same
/// zero-reference way every sibling feature's own <c>ICurrentTenantProvider</c> does - this
/// feature's own domain model did not need to change shape to support that.
/// </para>
/// </summary>
public sealed class DocumentModule : IBiologModule
{
    public string Name => "Document";
    public IReadOnlyCollection<string> DependsOn => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDocumentSessionStore, InMemoryDocumentSessionStore>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
