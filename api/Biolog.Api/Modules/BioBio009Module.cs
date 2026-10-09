using Biolog.Api.Tenancy;
using Biolog.Features.BiometricRetention.Api;
using Biolog.Features.BiometricRetention.Audit;
using Biolog.Features.BiometricRetention.Domain;
using Biolog.Features.BiometricRetention.Service;
using Biolog.Features.FaceEnrolment.Service;
using Biolog.Features.FaceSearch.Service;
using Biolog.Features.FingerprintEnrolment.Service;
using Biolog.Features.FingerprintSearch.Service;

namespace Biolog.Api.Modules;

/// <summary>
/// BIO-BIO-009: biometric retention policy (AC01), audited template deletion (AC02), raw-image
/// retention default-off (AC03) and deletion propagation to 1:N identification gallery caches
/// (AC04). Registers <see cref="RetentionPolicyService"/>/<see cref="BiometricDeletionService"/>
/// plus the in-memory policy/audit implementations, the same thin-adapter shape every prior
/// BIO-BIO module established.
///
/// <b>ADR-0006: this is an authorization-decision use, not audit attribution.</b> Zainab's round-1
/// security review (ZA-BIO009-01, Critical) correctly found that gating retention-policy writes
/// and template deletion on a bare <c>X-Actor-Permission</c> header compare is exactly
/// <c>ZA-IDN006-01</c>'s shape: any caller can assert any permission. Fixed by adopting the
/// identical sanctioned pattern BIO-ACC-001/BIO-ACC-003/BIO-MOB-005 already use for their own
/// authorization-decision endpoints: <see cref="ActorProvenanceGate"/> (ADR-0006 Decision 3's
/// HMAC-bound provenance proof, a fresh project-local copy) verifies the asserted actor id
/// BEFORE <see cref="PermissionGatewayAdapter"/> - wrapping BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c>, a REAL grant lookup - is ever consulted.
/// Both checks live entirely in the endpoint handlers below, never inside
/// <see cref="RetentionPolicyService"/>/<see cref="BiometricDeletionService"/> themselves (see
/// those types' own headers) - the identical "service takes zero permission parameter, the
/// endpoint gates" shape BIO-MOB-005's own <c>EnrolmentPackageService.SetCapturePolicyAsync</c>
/// already uses.
///
/// <b>The deletion gateway.</b> <see cref="TemplateDeletionGatewayAdapter"/> is the concrete
/// implementation of <c>Biolog.Features.BiometricRetention.Domain.ITemplateDeletionGateway</c> -
/// this feature's own narrow seam onto BIO-BIO-002/003/006/007's real stores (see that
/// interface's own header). None of those four stores exposed a delete/remove capability when
/// this story was first submitted (verified directly) - a minimal, additive extension of all four
/// was requested from and approved by Kemi (hive query/reply, 2026-10-06, same shape as
/// BIO-IDN-004's own precedent) and is now wired for real:
/// <list type="bullet">
/// <item><see cref="TemplateDeletionGatewayAdapter.DeleteFingerprintSessionAsync"/> calls
/// BIO-BIO-002's own <see cref="FingerprintEnrolmentService.DeleteSessionAsync"/>, which reuses
/// that story's EXISTING shared lock/audit infrastructure and records its own audit event - a
/// concurrent capture-vs-delete race on the same session is genuinely serialized by that shared
/// lock. It returns the deleted session's own <c>PersonId</c>, resolved from the record itself,
/// never the caller's own assertion (Zainab's round-1 finding ZA-BIO009-01 on
/// <see cref="Service.BiometricDeletionService"/>'s own former caller-supplied <c>PersonId</c> -
/// see that service's header).</item>
/// <item><see cref="TemplateDeletionGatewayAdapter.DeleteFaceReferenceAsync"/> calls BIO-BIO-003's
/// own <see cref="FaceEnrolmentService.DeleteReferenceAsync"/> - a plain, unaudited, unlocked
/// persistence wrapper (that service has no lock/audit infrastructure at all today, a deliberate
/// original scope boundary - see its own doc comment), likewise returning the resolved
/// <c>PersonId</c>. Both gallery-removal methods call BIO-BIO-006's/BIO-BIO-007's own equally
/// plain <c>RemoveFromGalleryAsync</c> wrappers, using only that resolved <c>PersonId</c>.</item>
/// </list>
/// For those three plain wrappers, <see cref="Service.BiometricDeletionService"/> - not any of
/// the three sibling stores - is the one place the full ADR-0003 Decision 7 discipline (lock,
/// audit-before-commit, ambiguous-throw verification via this feature's OWN
/// <see cref="IBiometricRetentionAuditSink"/>, <see cref="CancellationToken.None"/> past the
/// confirmed audit point) exists for this operation, recorded once as this feature's own coherent
/// retention-deletion event rather than four fragmented, inconsistent partial records.
///
/// <b>AC05.</b> Round 3 (Seyi's BIO-BIO-009-F03): a real <c>web/features/bio-bio-009/</c> control
/// surface now reaches this module through <c>BiometricService</c>'s own
/// <c>manageRetentionPolicy</c>/<c>deleteBiometricRecord</c> ports, mounted on <c>/fingerprints</c>
/// - the module genuinely reaches HYBRID (BIO-UX-005-AC02), the same per-port LIVE-switch shape
/// BIO-BIO-008's own <c>checkLiveness</c> established. See
/// docs/features/bio-bio-009/README.md's own "AC05" section.
///
/// <b>Round 3 (Seyi/Zainab): AC03 is now enforced at the raw-image write boundary, and AC04's
/// gallery eviction is now durably recoverable, not best-effort-and-silent.</b> See
/// <c>Biolog.Features.FingerprintEnrolment.Domain.IRawImageRetentionAuthority</c> and
/// <see cref="Service.BiometricDeletionService"/>'s own headers.
/// </summary>
public sealed class BioBio009Module : IBiologModule
{
    /// <summary>AC01/AC03's required permission - configuring retention policy and authorising
    /// raw-image retention are the same class of decision.</summary>
    public const string ManageRetentionPolicyPermission = "biometric.retention.policy.manage";

    /// <summary>AC02/AC04's required permission.</summary>
    public const string DeleteBiometricRecordPermission = "biometric.retention.delete";

    public string Name => "BiometricRetention";

    public IReadOnlyCollection<string> DependsOn =>
        ["Tenant", "Rbac", "FingerprintEnrolment", "FaceEnrolment", "FingerprintSearch", "FaceSearch"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IRetentionPolicy, InMemoryRetentionPolicy>();
        services.AddSingleton<IRawImageRetentionPolicy, InMemoryRawImageRetentionPolicy>();
        services.AddSingleton<IBiometricRetentionAuditSink, InMemoryBiometricRetentionAuditSink>();
        services.AddSingleton<BiometricRetentionLockRegistry>();
        // Round 4 (god's dispatch): a file-backed store, not an in-memory one - the pending
        // obligation must survive a process restart, the whole point of calling it durable.
        services.AddSingleton(_ => PendingGalleryCleanupStoreProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<RetentionPolicyService>();

        // BIO-BIO-009-AC03/F01: the adapter BIO-BIO-002's own IRawImageRetentionAuthority seam
        // resolves against, wrapping this feature's REAL tenant policy above. Singleton because
        // FingerprintEnrolmentService (which consumes it) is itself Singleton - a Scoped
        // dependency here would be the captive-dependency hazard every other registration in
        // this module documents the other way round.
        services.AddSingleton<Biolog.Features.FingerprintEnrolment.Domain.IRawImageRetentionAuthority>(
            sp => new RawImageRetentionAuthorityAdapter(sp.GetRequiredService<IRawImageRetentionPolicy>()));

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();
        // Scoped: BIO-IDN-006's own PermissionService is registered Scoped (it depends on a
        // per-request tenant seam) - a Singleton here would capture a Scoped dependency.
        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));

        // Scoped, not Singleton: FaceEnrolmentService/FingerprintSearchService/FaceSearchService
        // are all registered Scoped by their own modules (each ultimately depends on a
        // per-request tenant/permission seam) - a Singleton here would capture a Scoped
        // dependency (the exact captive-dependency hazard every prior BIO-BIO module documents
        // for its own service).
        services.AddScoped<ITemplateDeletionGateway>(sp => new TemplateDeletionGatewayAdapter(
            sp.GetRequiredService<FingerprintEnrolmentService>(),
            sp.GetRequiredService<FaceEnrolmentService>(),
            sp.GetRequiredService<FingerprintSearchService>(),
            sp.GetRequiredService<FaceSearchService>()));
        services.AddScoped<BiometricDeletionService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/biometrics/retention/policy", async (
            string category, RetentionPolicyService service, ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            try
            {
                var tenantId = RequireTenant(tenantAccessor);
                if (!TryParseCategory(category, out var parsed))
                {
                    return UnknownCategoryProblem(category);
                }

                var setting = await service.GetRetentionPolicyAsync(tenantId, parsed, cancellationToken);
                return Results.Ok(new { category, retentionDays = setting.RetentionDays, isExplicitlyConfigured = setting.IsExplicitlyConfigured });
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
        });

        endpoints.MapPost("/api/v1/biometrics/retention/policy", async (
            RetentionPolicyRequest? request, RetentionPolicyService service, ITenantContextAccessor tenantAccessor, HttpRequest httpRequest,
            IPermissionGateway permissionGateway, ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            try
            {
                var tenantId = RequireTenant(tenantAccessor);
                var actorId = ActorResolver.Resolve(httpRequest);
                await AuthorizeAsync(httpRequest, actorId, tenantId, ManageRetentionPolicyPermission, provenanceGate, permissionGateway, cancellationToken);

                if (request is null || !TryParseCategory(request.Category, out var category))
                {
                    return UnknownCategoryProblem(request?.Category);
                }

                await service.SetRetentionPolicyAsync(tenantId, category, request.RetentionDays, actorId, cancellationToken);
                return Results.Ok(new { category = request.Category, retentionDays = request.RetentionDays });
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (BiometricRetentionException ex)
            {
                return RetentionProblem(ex);
            }
        });

        endpoints.MapGet("/api/v1/biometrics/retention/raw-image-authorization", async (
            RetentionPolicyService service, ITenantContextAccessor tenantAccessor, CancellationToken cancellationToken) =>
        {
            try
            {
                var tenantId = RequireTenant(tenantAccessor);
                var authorised = await service.IsRawImageRetentionAuthorisedAsync(tenantId, cancellationToken);
                return Results.Ok(new { authorised });
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
        });

        endpoints.MapPost("/api/v1/biometrics/retention/raw-image-authorization", async (
            RawImageAuthorizationRequest? request, RetentionPolicyService service, ITenantContextAccessor tenantAccessor,
            HttpRequest httpRequest, IPermissionGateway permissionGateway, ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            try
            {
                var tenantId = RequireTenant(tenantAccessor);
                var actorId = ActorResolver.Resolve(httpRequest);
                await AuthorizeAsync(httpRequest, actorId, tenantId, ManageRetentionPolicyPermission, provenanceGate, permissionGateway, cancellationToken);

                var authorised = request?.Authorised ?? false;
                await service.SetRawImageRetentionAuthorisedAsync(tenantId, authorised, actorId, cancellationToken);
                return Results.Ok(new { authorised });
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (BiometricRetentionException ex)
            {
                return RetentionProblem(ex);
            }
        });

        endpoints.MapPost("/api/v1/biometrics/retention/delete", async (
            DeleteTemplateRequest? request, BiometricDeletionService service, ITenantContextAccessor tenantAccessor, HttpRequest httpRequest,
            IPermissionGateway permissionGateway, ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            try
            {
                var tenantId = RequireTenant(tenantAccessor);
                var actorId = ActorResolver.Resolve(httpRequest);
                await AuthorizeAsync(httpRequest, actorId, tenantId, DeleteBiometricRecordPermission, provenanceGate, permissionGateway, cancellationToken);

                if (request is null || !TryParseKind(request.Kind, out var kind))
                {
                    return Results.Problem(
                        title: "unknown_biometric_record_kind", detail: $"'{request?.Kind}' is not a known record kind.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var deletionRequest = new TemplateDeletionRequest
                {
                    TenantId = tenantId,
                    Kind = kind,
                    RecordId = request.RecordId,
                    ActorId = actorId,
                    Reason = request.Reason,
                };

                var result = await service.DeleteAsync(deletionRequest, cancellationToken);
                return Results.Ok(new { outcome = result.Outcome.ToString(), galleryPropagated = result.GalleryPropagated });
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (BiometricRetentionException ex)
            {
                return RetentionProblem(ex);
            }
        });

        // BIO-BIO-009-AC04/ZB-BIO009-06: the recovery path for a prior DeletedPendingGalleryCleanup
        // outcome. Same permission as deletion itself - reconciling a cleanup obligation is part
        // of the same privileged operation, not a lesser one.
        endpoints.MapPost("/api/v1/biometrics/retention/reconcile-gallery-cleanup", async (
            BiometricDeletionService service, ITenantContextAccessor tenantAccessor, HttpRequest httpRequest,
            IPermissionGateway permissionGateway, ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            try
            {
                var tenantId = RequireTenant(tenantAccessor);
                var actorId = ActorResolver.Resolve(httpRequest);
                await AuthorizeAsync(httpRequest, actorId, tenantId, DeleteBiometricRecordPermission, provenanceGate, permissionGateway, cancellationToken);

                var result = await service.ReconcilePendingGalleryCleanupAsync(tenantId, cancellationToken);
                return Results.Ok(new { resolved = result.Resolved, stillPending = result.StillPending });
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (BiometricRetentionException ex)
            {
                return RetentionProblem(ex);
            }
        });
    }

    /// <summary>ADR-0006 Decision 3: verify the asserted actor's provenance proof BEFORE
    /// consulting the real RBAC grant lookup - never the other way round, and never skipping
    /// either. Shared by every mutating endpoint above.</summary>
    private static async Task AuthorizeAsync(
        HttpRequest httpRequest, string actorId, string tenantId, string requiredPermission, ActorProvenanceGate provenanceGate,
        IPermissionGateway permissionGateway, CancellationToken cancellationToken)
    {
        provenanceGate.Require(httpRequest, actorId);

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actorId, requiredPermission, cancellationToken))
        {
            throw BiometricRetentionException.PermissionRequired(requiredPermission);
        }
    }

    private static bool TryParseCategory(string? wire, out RetentionCategory category)
    {
        switch (wire)
        {
            case "fingerprint_template":
                category = RetentionCategory.FingerprintTemplate;
                return true;
            case "face_template":
                category = RetentionCategory.FaceTemplate;
                return true;
            case "raw_image":
                category = RetentionCategory.RawImage;
                return true;
            default:
                category = default;
                return false;
        }
    }

    private static bool TryParseKind(string? wire, out BiometricRecordKind kind)
    {
        switch (wire)
        {
            case "fingerprint_session":
                kind = BiometricRecordKind.FingerprintSession;
                return true;
            case "face_reference":
                kind = BiometricRecordKind.FaceReference;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static string RequireTenant(ITenantContextAccessor tenantAccessor) =>
        tenantAccessor.Context?.TenantId ?? throw new TenantContextMissingException();

    private static IResult TenantContextProblem(TenantContextMissingException ex) => Results.Problem(
        title: "tenant_context_missing", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);

    private static IResult UnknownCategoryProblem(string? category) => Results.Problem(
        title: "unknown_retention_category", detail: $"'{category}' is not a known retention category.",
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult RetentionProblem(BiometricRetentionException ex)
    {
        var statusCode = ex.Code switch
        {
            "permission_required" => StatusCodes.Status403Forbidden,
            "actor_required" => StatusCodes.Status400BadRequest,
            "actor_provenance_required" => StatusCodes.Status401Unauthorized,
            "invalid_retention_days" => StatusCodes.Status400BadRequest,
            "audit_unavailable" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest,
        };
        return Results.Problem(title: ex.Code, detail: ex.Message, statusCode: statusCode);
    }

    /// <summary>Wraps BIO-IDN-006's own already-registered <c>PermissionService.ActorHasPermissionAsync</c>
    /// directly - the same adapter shape BIO-MOB-005's own <c>PermissionGatewayAdapter</c>
    /// already uses.</summary>
    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }

    /// <summary>See this class's own header for the full rationale behind each method below.</summary>
    private sealed class TemplateDeletionGatewayAdapter(
        FingerprintEnrolmentService fingerprintEnrolmentService,
        FaceEnrolmentService faceEnrolmentService,
        FingerprintSearchService fingerprintSearchService,
        FaceSearchService faceSearchService) : ITemplateDeletionGateway
    {
        public async Task<string?> ResolveFingerprintSessionOwnerAsync(string tenantId, string sessionId, CancellationToken cancellationToken)
        {
            var session = await fingerprintEnrolmentService.GetSessionAsync(tenantId, sessionId, cancellationToken);
            return session?.PersonId;
        }

        public Task<string?> ResolveFaceReferenceOwnerAsync(string tenantId, string referenceId, CancellationToken cancellationToken) =>
            faceEnrolmentService.ResolveOwnerAsync(tenantId, referenceId, cancellationToken);

        public async Task<(DeletionOutcome Outcome, string? PersonId)> DeleteFingerprintSessionAsync(
            string tenantId, string sessionId, string reason, string actorId, CancellationToken cancellationToken)
        {
            var personId = await fingerprintEnrolmentService.DeleteSessionAsync(tenantId, sessionId, reason, actorId, cancellationToken);
            return personId is null ? (DeletionOutcome.NotFound, null) : (DeletionOutcome.Deleted, personId);
        }

        public async Task<(DeletionOutcome Outcome, string? PersonId)> DeleteFaceReferenceAsync(
            string tenantId, string referenceId, CancellationToken cancellationToken)
        {
            var personId = await faceEnrolmentService.DeleteReferenceAsync(tenantId, referenceId, cancellationToken);
            return personId is null ? (DeletionOutcome.NotFound, null) : (DeletionOutcome.Deleted, personId);
        }

        public Task<bool> RemoveFromFingerprintGalleryAsync(string tenantId, string personId, CancellationToken cancellationToken) =>
            fingerprintSearchService.RemoveFromGalleryAsync(tenantId, personId, cancellationToken);

        public Task<bool> RemoveFromFaceGalleryAsync(string tenantId, string personId, CancellationToken cancellationToken) =>
            faceSearchService.RemoveFromGalleryAsync(tenantId, personId, cancellationToken);
    }

    /// <summary>BIO-BIO-009-AC03/F01: wraps this feature's own real <see cref="IRawImageRetentionPolicy"/>
    /// to satisfy BIO-BIO-002's own <c>IRawImageRetentionAuthority</c> seam - see that interface's
    /// header for why the dependency points this direction (BIO-BIO-002 cannot reference this
    /// later story directly).</summary>
    private sealed class RawImageRetentionAuthorityAdapter(IRawImageRetentionPolicy policy)
        : Biolog.Features.FingerprintEnrolment.Domain.IRawImageRetentionAuthority
    {
        public Task<bool> IsAuthorisedAsync(string tenantId, CancellationToken cancellationToken) =>
            policy.IsAuthorisedAsync(tenantId, cancellationToken);
    }
}

/// <summary>Request body for the retention-policy endpoint. <c>Category</c> is the wire string
/// (<c>fingerprint_template</c>/<c>face_template</c>/<c>raw_image</c>), not the enum member.</summary>
public sealed record RetentionPolicyRequest(string Category, int RetentionDays);

public sealed record RawImageAuthorizationRequest(bool Authorised);

/// <summary>Request body for the deletion endpoint. <c>Kind</c> is the wire string
/// (<c>fingerprint_session</c>/<c>face_reference</c>), not the enum member. Deliberately carries
/// no <c>PersonId</c> - see <c>TemplateDeletionRequest</c>'s own header (ZA-BIO009-01).</summary>
public sealed record DeleteTemplateRequest(string Kind, string RecordId, string Reason);
