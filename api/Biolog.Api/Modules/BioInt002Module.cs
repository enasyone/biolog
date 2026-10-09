using Biolog.Api.Tenancy;
using Biolog.Features.Integrations;
using Biolog.Features.Integrations.Api;
using Biolog.Features.Integrations.Audit;
using Biolog.Features.Integrations.Domain;
using Biolog.Features.Integrations.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// SIS/HR/ERP import framework (BIO-INT-002), registered the same way every sibling
/// identity-wave module wraps its feature project.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <see cref="PermissionGatewayAdapter"/> wraps BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly - this story's RBAC integration seam.
///
/// <see cref="PersonGatewayAdapter"/> wraps BIO-IDN-001's own already-registered (internal,
/// <c>InternalsVisibleTo Biolog.Api</c>), Scoped <c>PersonService</c> directly - reachable here
/// only because that project's own csproj grants this assembly internals visibility, the same
/// cross-story gateway pattern every sibling story uses. Deliberately exposes only
/// <c>RegisterAsync</c>/<c>GetAsync</c> - see <c>Domain.IPersonGateway</c>'s own header (AC04).
/// Both adapters translate the wrapped feature's own domain error type into this feature's own
/// <see cref="ImportError"/> so <c>ImportEndpoints</c>'s single catch clause can map it to a
/// problem response.
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioIdn006Module"/> (registered as
/// <c>"Rbac"</c>) and <see cref="BioIdn001Module"/> (registered as <c>"PersonRegistry"</c>).
/// </summary>
public sealed class BioInt002Module : IBiologModule
{
    public string Name => "Integrations";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "Rbac", "PersonRegistry"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<IImportSourceMappingRepository, InMemoryImportSourceMappingRepository>();
        services.AddSingleton<IImportBatchRepository, InMemoryImportBatchRepository>();
        services.AddSingleton<IExternalIdentityLinkStore, InMemoryExternalIdentityLinkStore>();
        services.AddSingleton<IImportAuditSink, InMemoryImportAuditSink>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<IOrphanedPersonLedger, InMemoryOrphanedPersonLedger>();
        services.AddSingleton<IImportRowProvenanceLedger, InMemoryImportRowProvenanceLedger>();
        services.AddSingleton<ImportLockRegistry>();

        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddScoped<IPersonGateway>(
            sp => new PersonGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));

        services.AddScoped<ImportMappingService>();
        services.AddScoped<ImportBatchService>();

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ImportEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }

    private sealed class PersonGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService) : IPersonGateway
    {
        public async Task<PersonView> CreateAsync(CreatePersonFields fields, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default)
        {
            if (!Biolog.Features.PersonRegistry.Audit.ActorTypeExtensions.TryParse(actorTypeWire, out var actorType))
            {
                actorType = Biolog.Features.PersonRegistry.Audit.ActorType.ServiceAccount;
            }

            try
            {
                var person = await personService.RegisterAsync(
                    new Biolog.Features.PersonRegistry.Api.RegisterPersonRequest
                    {
                        Title = fields.Title,
                        FirstName = fields.FirstName,
                        MiddleName = fields.MiddleName,
                        LastName = fields.LastName,
                        PreferredName = fields.PreferredName,
                        BornOn = fields.BornOn,
                    },
                    new Biolog.Features.PersonRegistry.Audit.AuditActor(actorType, actorId),
                    correlationId,
                    cancellationToken);
                return MapToView(person);
            }
            catch (Biolog.Features.PersonRegistry.Domain.PersonError ex)
            {
                throw Translate(ex);
            }
        }

        public async Task<PersonView?> GetAsync(string personId, CancellationToken cancellationToken = default)
        {
            var person = await personService.GetAsync(personId, cancellationToken);
            return person is null ? null : MapToView(person);
        }

        private static PersonView MapToView(Biolog.Features.PersonRegistry.Domain.Person p) => new()
        {
            Id = p.Id,
            TenantId = p.TenantId,
            Title = p.Title,
            FirstName = p.FirstName,
            MiddleName = p.MiddleName,
            LastName = p.LastName,
            PreferredName = p.PreferredName,
            BornOn = p.BornOn,
            StatusWire = Biolog.Features.PersonRegistry.Domain.PersonStatusExtensions.ToWireString(p.Status),
        };

        private static ImportError Translate(Biolog.Features.PersonRegistry.Domain.PersonError ex) =>
            new(ex.Message, ex.Code, ex.Status, ex.Detail);
    }
}
