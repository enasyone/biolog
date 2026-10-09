using Biolog.Api.Tenancy;
using Biolog.Features.PhotoQuality;
using Biolog.Features.PhotoQuality.Api;
using Biolog.Features.PhotoQuality.Audit;
using Biolog.Features.PhotoQuality.Domain;
using Biolog.Features.PhotoQuality.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Photo quality and biometric-suitability evaluation (BIO-PHO-003), registered the same way
/// every sibling identity/card-wave module wraps its feature project - written directly here,
/// in <c>Biolog.Api.Modules</c>, from the start, so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <b><see cref="PersonExistenceGatewayAdapter"/></b> wraps BIO-IDN-001's own already-registered
/// <c>PersonService.GetAsync</c> directly - the same tenant-scoping-for-free reasoning every
/// prior gateway's header documents.
///
/// <b><see cref="PhotoMasterPromotionGatewayAdapter"/></b> wraps BIO-PHO-001's own
/// already-registered <c>PhotoMasterService.AppendAsync</c> - the identical adapter shape
/// BIO-PHO-002's own <c>PhotoMasterPromotionGatewayAdapter</c> already established, including
/// the same cross-feature <c>ActorType</c> round-trip through each side's own wire-string
/// (never a raw numeric cast - an enum member reordered in either file alone would silently
/// misattribute an actor type under a cast, but <c>TryParse</c> fails loudly instead).
///
/// <b>ZB-PHO003-03/04 and SY-PHO003-07, resolved by Kemi's architectural ruling (not another
/// adapter patch).</b> Rounds 3-5 tried to RECONSTRUCT "did this exact attempt already promote"
/// from BIO-PHO-001's own post-hoc state (an exact attempt-id audit lookup, then a
/// repository-existence confirmation) - each fix closed one failure shape and immediately
/// exposed an adjacent one, because <c>PhotoMaster</c> genuinely has no caller-supplied identity
/// field and inferring ownership from an external system's own observable state can never be
/// fully disambiguated from outside it. Kemi's ruling: stop reconstructing, start REMEMBERING -
/// <see cref="PhotoQualityService"/> now owns a local <c>IIdempotencyStore</c> (see its own
/// header) instead, and <see cref="PhotoMasterPromotionGatewayAdapter"/> shrinks back to exactly
/// the shape BIO-PHO-002's own adapter already has - just <c>PromoteAsync</c>, nothing else.
///
/// <b><see cref="BiometricSuitabilityGatewayAdapter"/> is the one genuinely new cross-feature
/// wiring this story adds.</b> Wraps BIO-BIO-003's own already-registered, PUBLIC
/// <c>IFaceQualityChecker.AssessAsync</c> and <c>ILivenessDetector.DetectAsync</c> directly - a
/// real face-count/seven-check quality assessment plus a real liveness verdict, never a
/// feature-local re-implementation of either. Builds a <c>BiometricSample</c> (BIO-BIO-001's
/// own provider-neutral capture type) from the same bytes the enrolment staff member uploaded,
/// treating the candidate photo itself as the biometric sample - the same source-of-truth a
/// later BIO-BIO-003-driven enrolment would eventually use. Read-only: never calls
/// <c>FaceEnrolmentService.CaptureAsync</c>, so no <c>FaceBiometricReference</c> is ever
/// committed from this story's own write path (see <see cref="PhotoQualityService"/>'s header
/// "Deliberately does not create a BIO-BIO-003 FaceBiometricReference").
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioIdn001Module"/> (registered as
/// <c>"PersonRegistry"</c>), <see cref="BioPho001Module"/> (registered as <c>"Photography"</c>),
/// <see cref="BioBio003Module"/> (registered as <c>"FaceEnrolment"</c>) and
/// <see cref="BioIdn006Module"/> (registered as <c>"Rbac"</c>).
///
/// <b>ZB-PHO003-01 (Zainab, security round 1, Critical), fixed.</b> <see cref="ActorProvenanceGate"/>
/// (a fresh, feature-local HMAC-provenance copy - see its own header) and
/// <see cref="PermissionGatewayAdapter"/> (wrapping BIO-IDN-006's own already-registered,
/// internal <c>PermissionService.ActorHasPermissionAsync</c>) are now required on every one of
/// this story's five routes, in <c>PhotoQualityEndpoints.AuthorizeAsync</c> - reads included,
/// since biometric-suitability verdicts/decision reasons/photo metadata are sensitive enough to
/// need the same real grant check as a write (just a lesser-privileged permission key). The demo
/// web bridge's grant for this story's "enrolment staff" actor is seeded in
/// <see cref="Configure"/>, the identical "seed data, not provisioning logic" shape
/// <c>BioAtt003Module</c>/<c>BioTen003Module</c> already established.
/// </summary>
public sealed class BioPho003Module : IBiologModule
{
    public string Name => "PhotoQuality";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry", "Photography", "FaceEnrolment", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddScoped<IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));

        services.AddScoped<IPhotoMasterPromotionGateway>(
            sp => new PhotoMasterPromotionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Photography.PhotoMasterService>()));

        services.AddScoped<IBiometricSuitabilityGateway>(
            sp => new BiometricSuitabilityGatewayAdapter(
                sp.GetRequiredService<Biolog.Features.FaceEnrolment.Quality.IFaceQualityChecker>(),
                sp.GetRequiredService<Biolog.Features.FaceEnrolment.Liveness.ILivenessDetector>()));

        services.AddSingleton<IPhotoQualityCaptureRepository, InMemoryPhotoQualityCaptureRepository>();
        services.AddSingleton<Biolog.Features.PhotoQuality.Audit.IPhotoQualityAuditSink, Biolog.Features.PhotoQuality.Audit.InMemoryPhotoQualityAuditSink>();
        services.AddSingleton<PhotoQualityCaptureLockRegistry>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();
        // Scoped: BIO-IDN-006's own PermissionService is registered Scoped (it depends on a
        // per-request tenant seam) - a Singleton here would capture a Scoped dependency.
        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));

        services.AddScoped<PhotoQualityService>();
    }

    /// <summary>
    /// TD-WEB-PROVENANCE-BRIDGE-REVERIFY's own share of the obligation (same pattern as
    /// <c>BioAtt003Module</c>'s/<c>BioTen003Module</c>'s equivalent fix): a real seeded
    /// <see cref="Biolog.Features.Rbac.Domain.RolePermissionGrant"/> for this story's own three
    /// permission keys, scoped to the one hardcoded <c>WebSessionBridge:SyntheticTenantId</c>
    /// synthetic tenant - without it no LIVE browser session could ever reach a real "granted"
    /// result through this gate. Granted to <see cref="Biolog.Features.Rbac.Domain.RoleType.Enrolment"/>
    /// (the "staff" demo role) - this user story is literally "As enrolment staff" - not
    /// <c>TenantAdmin</c>, so the LIVE positive proof exercises the same role an operator doing
    /// this work would actually hold. Fail-closed/inert unless that configuration key is set,
    /// idempotent, writes directly to the repository (never through
    /// <c>PermissionService.SetGrantAsync</c>, which expects a real administrative actor and
    /// audit trail a fixed startup fixture has neither of).
    /// </summary>
    public void Configure(IApplicationBuilder app)
    {
        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var tenantId = configuration["WebSessionBridge:SyntheticTenantId"];
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        var grants = app.ApplicationServices.GetRequiredService<Biolog.Features.Rbac.Domain.IRolePermissionGrantRepository>();
        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.Enrolment, PhotoQualityPermissions.Submit);
        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.Enrolment, PhotoQualityPermissions.Decide);
        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.Enrolment, PhotoQualityPermissions.View);
    }

    private static void SeedGrant(Biolog.Features.Rbac.Domain.IRolePermissionGrantRepository grants, string tenantId, Biolog.Features.Rbac.Domain.RoleType role, string permissionKey)
    {
        if (grants.GetAsync(tenantId, role, permissionKey).GetAwaiter().GetResult() is not null)
        {
            return;
        }

        var now = Biolog.Features.Rbac.Identifiers.UtcInstant.Now();
        grants.UpsertAsync(new Biolog.Features.Rbac.Domain.RolePermissionGrant(tenantId, role, permissionKey)
        {
            Granted = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        }).GetAwaiter().GetResult();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PhotoQualityEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PersonExistenceGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService) : IPersonExistenceGateway
    {
        public async Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
            await personService.GetAsync(personId, cancellationToken) is not null;
    }

    private sealed class PhotoMasterPromotionGatewayAdapter(Biolog.Features.Photography.PhotoMasterService photoMasterService) : IPhotoMasterPromotionGateway
    {
        public async Task<int> PromoteAsync(
            string personId, byte[] content, Biolog.Features.PhotoQuality.Audit.AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
        {
            if (!Biolog.Features.Photography.Audit.ActorTypeExtensions.TryParse(actor.Type.ToWireString(), out var photographyActorType))
            {
                photographyActorType = Biolog.Features.Photography.Audit.ActorType.Unknown;
            }

            var photographyActor = new Biolog.Features.Photography.Audit.AuditActor(photographyActorType, actor.Id);
            var master = await photoMasterService.AppendAsync(personId, content, photographyActor, correlationId, cancellationToken);
            return master.Version;
        }
    }

    private sealed class BiometricSuitabilityGatewayAdapter(
        Biolog.Features.FaceEnrolment.Quality.IFaceQualityChecker qualityChecker,
        Biolog.Features.FaceEnrolment.Liveness.ILivenessDetector livenessDetector) : IBiometricSuitabilityGateway
    {
        public async Task<BiometricSuitabilityResult> AssessAsync(byte[] content, CancellationToken cancellationToken = default)
        {
            var sample = new Biolog.Features.Biometrics.Providers.BiometricSample
            {
                Payload = content,
                ContentType = "image/*",
                CapturedAtUtc = Biolog.Features.PhotoQuality.Identifiers.UtcInstant.Now(),
            };

            var assessment = await qualityChecker.AssessAsync(sample, cancellationToken);
            var liveness = await livenessDetector.DetectAsync(sample, cancellationToken);

            var reasons = assessment.RecaptureGuidance.ToList();
            var passed = assessment.IsApproved && liveness.IsLive;
            if (assessment.IsApproved && !liveness.IsLive)
            {
                reasons.Add("liveness_check_failed: the capture did not pass liveness verification.");
            }

            return new BiometricSuitabilityResult(passed, reasons);
        }
    }

    /// <summary>Wraps BIO-IDN-006's own already-registered, internal <c>PermissionService.ActorHasPermissionAsync</c> directly - the same adapter shape <c>BioBio009Module</c>'s own <c>PermissionGatewayAdapter</c> already established.</summary>
    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }
}
