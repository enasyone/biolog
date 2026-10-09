using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;
using Biolog.Features.MobileVerification;
using Biolog.Features.MobileVerification.Api;
using Biolog.Features.MobileVerification.Audit;
using Biolog.Features.MobileVerification.Domain;

namespace Biolog.Api.Modules;

/// <summary>
/// Mobile identity/card/face verification flows (BIO-MOB-004), registered the same way every
/// sibling module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start, so the dependency stays one-way.
///
/// <b>The ambient-tenant wall (device-authenticated routes), mirroring Kemi's BIO-MOB-003
/// ruling and its identical extension to BIO-ACC-003 and BIO-MOB-005.</b>
/// <see cref="DeviceTenantResolutionGatewayAdapter"/> resolves the tenant from the authenticated
/// device's own registered record via the existing <see cref="ICrossTenantDeviceLookup"/> escape
/// hatch on <c>DeviceRegistry</c> (already reviewed three times over - this is a fourth,
/// independent feature-side gateway consuming the same seam, not a new grant).
///
/// <b>AC01/AC04's real seam onto BIO-ACC-002.</b> <see cref="AccessDecisionGatewayAdapter"/>
/// manually constructs BIO-ACC-002's own internal <c>AccessDecisionService</c> per call - never
/// resolved from the container directly, never via the reflection-based DI activator -
/// substituting a fixed tenant provider bound to the resolved device's own tenant for that
/// service's <c>ICurrentTenantProvider</c> dependency. BIO-ACC-002's own container-level
/// registration of that interface is wired only to the host/principal channel
/// (<see cref="ITenantContextAccessor"/>), which a device-authenticated mobile request never has
/// (ADR-0002 Decision 1). Every OTHER dependency (the shared Singleton event repository/audit
/// sink/lock registry, and the Scoped person/policy/fingerprint/face gateways) is pulled from
/// this SAME request's <see cref="IServiceProvider"/> - the identical instances BIO-ACC-002's own
/// endpoint would have used, so idempotency locking and stored-decision replay behave exactly as
/// BIO-ACC-002 itself already reviewed. This never touches BIO-ACC-002's own
/// <c>ActorProvenanceGate</c>/<c>ActorResolver</c> or its <c>AccessDecisions:ActorProvenanceSecret</c>
/// at all - those gate only BIO-ACC-002's own public HTTP route, not the underlying service a
/// sibling in-process module calls directly, the identical pattern BIO-ACC-002 itself already
/// uses to call BIO-BIO-004/BIO-BIO-005/BIO-ACC-001 (see <c>BioAcc002Module.cs</c>'s own header).
/// <see cref="VerificationDecisionResult"/> deliberately carries no biometric evidence/score field
/// at all (AC01) - <c>AccessDecisionEvent.BiometricEvidence</c> is read by nothing here.
///
/// <b>AC03 - device-scoped verification role, escalated before building (hive thread
/// <c>conv-bio-mob-004-ac03</c>, 2026-09-30).</b> Mobile has no human officer/actor identity
/// today (ADR-0006 never mentions a device principal; see
/// <c>Domain/VerificationDeviceRole.cs</c>'s own header for the full reasoning), so this feature
/// gates its two mobile-facing actions (execute a decision request; read a person's verification
/// history) by a role attached to the already-authenticated DEVICE, defaulting conservatively to
/// <c>Officer</c> (execute only). The role ASSIGNMENT write is this feature's one genuinely
/// human-actor-RBAC-gated route, mirroring BIO-MOB-005's own <c>ManagePolicyPermissionKey</c>
/// pattern exactly via BIO-IDN-006's <c>Rbac</c> module.
///
/// Depends on <see cref="DeviceRegistryModule"/> (device auth/tenant resolution for the
/// mobile-facing routes), the <c>AccessDecisions</c> module (BIO-ACC-002, this story's real
/// decision engine), <see cref="TenantModule"/> (ambient tenant for the device-role write route)
/// and BIO-IDN-006's <c>Rbac</c> module (that same route's permission gate).
/// </summary>
public sealed class BioMob004Module : IBiologModule
{
    public string Name => "MobileVerification";
    public IReadOnlyCollection<string> DependsOn => ["DeviceRegistry", "AccessDecisions", "Tenant", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDeviceTenantResolutionGateway>(
            sp => new DeviceTenantResolutionGatewayAdapter(sp.GetRequiredService<ICrossTenantDeviceLookup>()));

        services.AddSingleton(_ => DeviceCredentialProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<IDeviceCredentialStore>(sp => sp.GetRequiredService<InMemoryDeviceCredentialStore>());
        services.AddScoped<IDeviceAuthenticator, HeaderDeviceAuthenticator>();

        services.AddScoped<Biolog.Features.MobileVerification.Tenancy.ICurrentTenantProvider>(
            sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();

        services.AddSingleton<IVerificationAuditSink, InMemoryVerificationAuditSink>();
        services.AddSingleton<IVerificationDeviceRoleRepository, InMemoryVerificationDeviceRoleRepository>();
        services.AddSingleton<IVerificationOverrideRepository, InMemoryVerificationOverrideRepository>();

        services.AddScoped<IAccessDecisionGateway>(sp => new AccessDecisionGatewayAdapter(sp));

        services.AddScoped<VerificationService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        VerificationEndpoints.Map(endpoints);
    }

    private sealed class DeviceTenantResolutionGatewayAdapter(ICrossTenantDeviceLookup crossTenantLookup) : IDeviceTenantResolutionGateway
    {
        public async Task<ResolvedDevice?> ResolveAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            var device = await crossTenantLookup.GetByIdAsync(deviceId, cancellationToken);
            return device is not null && device.Status == DeviceStatus.Active
                ? new ResolvedDevice(device.Id, device.TenantId)
                : null;
        }
    }

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : Biolog.Features.MobileVerification.Tenancy.ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class AccessDecisionGatewayAdapter(IServiceProvider serviceProvider) : IAccessDecisionGateway
    {
        public async Task<VerificationDecisionResult> DecideAsync(string tenantId, VerificationDecisionQuery query, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
        {
            var service = Build(tenantId);
            var request = new Biolog.Features.AccessDecisions.Api.DecideAccessRequest
            {
                PersonId = query.PersonId,
                ProfileKey = query.ProfileKey,
                CredentialClass = query.CredentialClass,
                LocationId = query.LocationId,
                OccurredAt = query.OccurredAt,
                PresentedCardStatus = query.PresentedCardStatus,
                PolicyKey = query.PolicyKey,
                FingerprintSample = ToTheirSample(query.FingerprintSample),
                FaceSample = ToTheirSample(query.FaceSample),
                IdempotencyKey = query.IdempotencyKey,
            };
            var theirActor = new Biolog.Features.AccessDecisions.Audit.AuditActor(ToTheirActorType(actor.Type), actor.Id);

            Biolog.Features.AccessDecisions.Domain.AccessDecisionEvent decision;
            try
            {
                decision = await service.DecideAsync(request, theirActor, correlationId, cancellationToken);
            }
            catch (Biolog.Features.AccessDecisions.Domain.AccessDecisionError error)
            {
                throw new VerificationError(error.Message, error.Code, error.Status, error.Detail)
                {
                    Errors = error.Errors?.Select(e => new ValidationErrorItem(e.Path, e.Code, e.Message)).ToList(),
                };
            }

            return ToOurResult(decision);
        }

        public async Task<IReadOnlyList<VerificationDecisionResult>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
        {
            var service = Build(tenantId);
            var decisions = await service.ListForPersonAsync(personId, cancellationToken);
            return [.. decisions.Select(ToOurResult)];
        }

        private Biolog.Features.AccessDecisions.AccessDecisionService Build(string tenantId) => new(
            new FixedTenantProvider(tenantId),
            serviceProvider.GetRequiredService<Biolog.Features.AccessDecisions.Domain.IAccessDecisionEventRepository>(),
            serviceProvider.GetRequiredService<Biolog.Features.AccessDecisions.Audit.IAccessDecisionAuditSink>(),
            serviceProvider.GetRequiredService<Biolog.Features.AccessDecisions.AccessDecisionLockRegistry>(),
            serviceProvider.GetRequiredService<Biolog.Features.AccessDecisions.Domain.IPersonExistenceGateway>(),
            serviceProvider.GetRequiredService<Biolog.Features.AccessDecisions.Domain.IPolicyDecisionGateway>(),
            serviceProvider.GetRequiredService<Biolog.Features.AccessDecisions.Domain.IFingerprintEvidenceGateway>(),
            serviceProvider.GetRequiredService<Biolog.Features.AccessDecisions.Domain.IFaceEvidenceGateway>());

        private static Biolog.Features.AccessDecisions.Api.BiometricSampleRequest? ToTheirSample(BiometricSampleInput? sample) =>
            sample is null ? null : new Biolog.Features.AccessDecisions.Api.BiometricSampleRequest
            {
                PayloadBase64 = Convert.ToBase64String(sample.Payload),
                ContentType = sample.ContentType,
                CapturedAt = sample.CapturedAtUtc,
            };

        private static Biolog.Features.AccessDecisions.Audit.ActorType ToTheirActorType(ActorType type) => type switch
        {
            ActorType.User => Biolog.Features.AccessDecisions.Audit.ActorType.User,
            ActorType.ServiceAccount => Biolog.Features.AccessDecisions.Audit.ActorType.ServiceAccount,
            ActorType.Device => Biolog.Features.AccessDecisions.Audit.ActorType.Device,
            ActorType.System => Biolog.Features.AccessDecisions.Audit.ActorType.System,
            ActorType.Anonymous => Biolog.Features.AccessDecisions.Audit.ActorType.Anonymous,
            _ => Biolog.Features.AccessDecisions.Audit.ActorType.Unknown,
        };

        /// <summary>AC01: only the fields a mobile officer legitimately needs. <c>e.BiometricEvidence</c> (raw per-modality score) is deliberately never read here.</summary>
        private static VerificationDecisionResult ToOurResult(Biolog.Features.AccessDecisions.Domain.AccessDecisionEvent e) => new()
        {
            EventId = e.EventId,
            PersonId = e.PersonId,
            Decision = e.Decision == Biolog.Features.AccessDecisions.Domain.AccessDecisionOutcome.Allow ? "allow" : "deny",
            ReasonCode = e.ReasonCode,
            PresentedAssuranceLevel = Biolog.Features.AccessDecisions.Domain.AssuranceLevelExtensions.ToWireString(e.PresentedAssuranceLevel),
            OccurredAt = e.OccurredAt,
            RecordedAt = e.RecordedAt,
            CorrelationId = e.CorrelationId,
            IdempotencyKey = e.IdempotencyKey,
        };

        private sealed record FixedTenantProvider(string TenantId) : Biolog.Features.AccessDecisions.Tenancy.ICurrentTenantProvider;
    }
}
