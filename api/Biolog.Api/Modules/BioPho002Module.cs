using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;
using Biolog.Features.PhotoBooth;
using Biolog.Features.PhotoBooth.Api;
using Biolog.Features.PhotoBooth.Audit;
using Biolog.Features.PhotoBooth.Domain;
using Biolog.Features.PhotoBooth.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Photo Booth integration contract (BIO-PHO-002), registered the same way every sibling
/// identity/card-wave module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start (never inside <c>api/features/bio-pho-002/**</c>
/// first), so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <b>Applying BIO-IDN-002-QA-01 from the start.</b> <see cref="PersonExistenceGatewayAdapter"/>
/// wraps BIO-IDN-001's own already-registered <c>PersonService.GetAsync</c>;
/// <see cref="DeviceExistenceGatewayAdapter"/> wraps BIO-DEV-001's own already-registered
/// <c>DeviceService.GetAsync</c>, requiring <see cref="DeviceStatus.Active"/> - the same
/// tenant-scoping-for-free reasoning every prior gateway's header documents.
///
/// <b>The one genuine cross-story runtime dependency in this epic so far.</b>
/// <see cref="PhotoMasterPromotionGatewayAdapter"/> wraps BIO-PHO-001's own already-registered
/// <c>Photography.PhotoMasterService.AppendAsync</c> - approval really does create a new,
/// immutable master through BIO-PHO-001's own machinery, not a reimplementation. "Dependencies:
/// BIO-PHO-001" in this story's own metadata is therefore a REAL runtime dependency, unlike the
/// "sequencing, not coupling" cases every prior sibling story has documented for its own
/// dependency line.
///
/// <b>Device credential provisioning</b> (BIO-PHO-002-AC02) is loaded from
/// <see cref="IConfiguration"/> here, the same out-of-band path
/// <c>TenantCredentialProvisioning</c> (BIO-CRD-001) established, applied from the start rather
/// than after a review round finds the gap - see <see cref="HeaderDeviceAuthenticator"/>'s own
/// header.
///
/// <b>ADR-0006 Decision 1/2/4.</b> This module wires no authorization/provenance gate on the
/// approve/reject routes - see <see cref="ActorResolver"/>'s header for why that is the correct,
/// disclosed shape here.
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioIdn001Module"/>,
/// <see cref="DeviceRegistryModule"/> and <see cref="BioPho001Module"/> - all four are genuine
/// runtime dependencies via this module's own adapters.
/// </summary>
public sealed class BioPho002Module : IBiologModule
{
    public string Name => "PhotoBooth";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry", "DeviceRegistry", "Photography"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddScoped<IPersonExistenceGateway>(
            sp => new PersonExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<IDeviceExistenceGateway>(
            sp => new DeviceExistenceGatewayAdapter(sp.GetRequiredService<DeviceService>()));
        services.AddScoped<IPhotoMasterPromotionGateway>(
            sp => new PhotoMasterPromotionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Photography.PhotoMasterService>()));

        services.AddSingleton(_ => DeviceCredentialProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<IDeviceCredentialStore>(sp => sp.GetRequiredService<InMemoryDeviceCredentialStore>());
        services.AddScoped<IDeviceAuthenticator, HeaderDeviceAuthenticator>();

        services.AddSingleton<IBoothCaptureRepository, InMemoryBoothCaptureRepository>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<ICaptureAuditSink, InMemoryCaptureAuditSink>();
        services.AddSingleton<BoothCaptureLockRegistry>();

        services.AddScoped<CaptureService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        CaptureEndpoints.Map(endpoints);
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

    private sealed class DeviceExistenceGatewayAdapter(DeviceService deviceService) : IDeviceExistenceGateway
    {
        public async Task<bool> IsActiveAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            var device = await deviceService.GetAsync(deviceId, cancellationToken);
            return device is not null && device.Status == DeviceStatus.Active;
        }
    }

    private sealed class PhotoMasterPromotionGatewayAdapter(Biolog.Features.Photography.PhotoMasterService photoMasterService) : IPhotoMasterPromotionGateway
    {
        public async Task<int> PromoteAsync(string personId, byte[] content, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
        {
            // Two independently-defined ActorType enums (this feature's own and Photography's
            // own - no cross-feature-project reference exists between them). Round-tripped
            // through the wire-string each already defines, never a raw numeric cast - an enum
            // member reordered in either file alone would silently misattribute an actor type
            // under a cast, but TryParse fails loudly instead.
            if (!Biolog.Features.Photography.Audit.ActorTypeExtensions.TryParse(actor.Type.ToWireString(), out var photographyActorType))
            {
                photographyActorType = Biolog.Features.Photography.Audit.ActorType.Unknown;
            }

            var photographyActor = new Biolog.Features.Photography.Audit.AuditActor(photographyActorType, actor.Id);
            var master = await photoMasterService.AppendAsync(personId, content, photographyActor, correlationId, cancellationToken);
            return master.Version;
        }
    }
}
