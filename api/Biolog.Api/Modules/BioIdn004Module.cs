using Biolog.Api.Tenancy;
using Biolog.Features.PersonLifecycle;
using Biolog.Features.PersonLifecycle.Api;
using Biolog.Features.PersonLifecycle.Audit;
using Biolog.Features.PersonLifecycle.Domain;
using Biolog.Features.PersonLifecycle.Tenancy;
using PersonRegistryPersonStatusExtensions = Biolog.Features.PersonRegistry.Domain.PersonStatusExtensions;

namespace Biolog.Api.Modules;

/// <summary>
/// Identity lifecycle and emergency suspension (BIO-IDN-004), registered the same way every
/// prior story's module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start (never inside <c>api/features/bio-idn-004/**</c>
/// first), so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> - ships in-story per ADR-0002 Decision 6's per-channel
/// accommodation-ending rule, the same reasoning every prior identity-wave story established.
///
/// <b>The write-capable gateway.</b> <see cref="PersonLifecycleGatewayAdapter"/> is the concrete
/// implementation of <c>Biolog.Features.PersonLifecycle.Domain.IPersonLifecycleGateway</c> -
/// this feature's own narrow seam onto BIO-IDN-001's real store, extended for a WRITE this time
/// (see that interface's header for the full rationale). It wraps BIO-IDN-001's own
/// <c>Biolog.Features.PersonRegistry.PersonService</c> (already registered Scoped by
/// <see cref="BioIdn001Module"/>) directly: both modules resolve from the SAME scoped DI
/// container for a given request, so this gateway gets BIO-IDN-001's own tenant scoping "for
/// free" exactly as the read-only gateways do. <c>PersonService.UpdateStatusAsync</c> throws its
/// own <c>PersonError</c> - a type this project has no reference to - so this adapter catches it
/// HERE (where both projects ARE referenced) and translates it into
/// <see cref="LifecycleChangeResult"/>'s plain, well-known error-code strings; both feature
/// projects intentionally use the SAME code strings (<c>person_not_found</c>,
/// <c>concurrency_conflict</c>, <c>invalid_status_transition</c>, <c>tenant_context_missing</c>)
/// so this translation is a direct pass-through of <c>PersonError.Code</c>, not a remapping.
///
/// Depends on <see cref="TenantModule"/> (for <see cref="TenantContextMiddleware"/> to have
/// already run) and on <see cref="BioIdn001Module"/> (a genuine runtime dependency via
/// <see cref="PersonLifecycleGatewayAdapter"/>).
/// </summary>
public sealed class BioIdn004Module : IBiologModule
{
    public string Name => "PersonLifecycle";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddSingleton<IPersonLifecycleAuditSink, InMemoryPersonLifecycleAuditSink>();
        services.AddSingleton<ICredentialAccessRevocationEventSink, InMemoryCredentialAccessRevocationEventSink>();
        services.AddSingleton<Biolog.Features.PersonLifecycle.Domain.IPendingSuspensionReconciliationStore, InMemoryPendingSuspensionReconciliationStore>();
        services.AddSingleton<PersonLifecycleLockRegistry>();
        services.AddScoped<Biolog.Features.PersonLifecycle.Domain.IPersonLifecycleGateway>(
            sp => new PersonLifecycleGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));
        services.AddScoped<PersonLifecycleService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PersonLifecycleEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PersonLifecycleGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService)
        : Biolog.Features.PersonLifecycle.Domain.IPersonLifecycleGateway
    {
        public async Task<PersonSnapshot?> GetSnapshotAsync(string personId, CancellationToken cancellationToken = default)
        {
            var person = await personService.GetAsync(personId, cancellationToken);
            if (person is null || !PersonStatusExtensions.TryParse(PersonRegistryPersonStatusExtensions.ToWireString(person.Status), out var status))
            {
                return null;
            }

            return new PersonSnapshot(person.Id, person.TenantId, status, person.Version);
        }

        public async Task<LifecycleChangeResult> ChangeStatusAsync(
            string personId, PersonStatus targetStatus, int expectedVersion, string actorId, string actorTypeWire, string correlationId,
            CancellationToken cancellationToken = default)
        {
            if (!Biolog.Features.PersonRegistry.Audit.ActorTypeExtensions.TryParse(actorTypeWire, out var actorType))
            {
                return LifecycleChangeResult.Failed("invalid_actor_type");
            }

            var actor = new Biolog.Features.PersonRegistry.Audit.AuditActor(actorType, actorId);
            var request = new Biolog.Features.PersonRegistry.Api.UpdatePersonStatusRequest { Status = targetStatus.ToWireString(), Version = expectedVersion };

            try
            {
                var updated = await personService.UpdateStatusAsync(personId, request, actor, correlationId, cancellationToken);
                return PersonStatusExtensions.TryParse(PersonRegistryPersonStatusExtensions.ToWireString(updated.Status), out var status)
                    ? LifecycleChangeResult.Ok(new PersonSnapshot(updated.Id, updated.TenantId, status, updated.Version))
                    : LifecycleChangeResult.Failed("gateway_status_mismatch");
            }
            catch (Biolog.Features.PersonRegistry.Domain.PersonError error)
            {
                return LifecycleChangeResult.Failed(error.Code);
            }
        }
    }
}
