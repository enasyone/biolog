using Biolog.Api.Tenancy;
using Biolog.Features.CardProduction;
using Biolog.Features.CardProduction.Api;
using Biolog.Features.CardProduction.Audit;
using Biolog.Features.CardProduction.Domain;
using Biolog.Features.CardProduction.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Card production jobs and print/encode verification (BIO-CRD-007), registered the same way
/// every sibling card module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start, so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <b><see cref="CardIssueGatewayAdapter"/> is the real cross-story dependency.</b> It wraps
/// BIO-CRD-005's own already-registered, internal <c>CardIssueService</c> directly - reachable
/// here only because that project's own csproj grants <c>InternalsVisibleTo Biolog.Api</c>, the
/// same seam every prior gateway adapter in this epic already established. Every call is
/// wrapped in a translation of BIO-CRD-005's own <c>CardIssueError</c> into this feature's own
/// <see cref="ProductionError"/> (same code/status/detail).
///
/// <b>Dependencies: BIO-CRD-006 and BIO-CRD-004 are sequencing, not coupling.</b> No AC here
/// names report/recover/replace or a card-designer canvas object; nothing in this module or the
/// feature project it wraps calls into either sibling's own module - the same "Dependencies in
/// story metadata is build-wave sequencing" precedent BIO-CRD-005's own module header already
/// established for BIO-CRD-002.
///
/// <b>ADR-0006 Decision 1/2/4.</b> This module wires no authorization/provenance gate - see
/// <see cref="Api.ActorResolver"/>'s header for why that is the correct, disclosed shape here.
///
/// <b>BIO-OPS-003-AC04 round 2 (SY-OPS003-02, Kemi's ruling).</b> <see cref="CardActivationBarrierAdapter"/>
/// wraps the new, SHARED, host-layer <c>Biolog.Api.Cards.CardActivationBarrierLockRegistry</c> -
/// registered ONCE, here (this module owns the finer-grained
/// <c>(tenant, person, cardClass, sequenceNumber)</c> key this shared lock reuses), the same
/// "register once, consume elsewhere" shape <c>DeviceRegistryModule</c>'s own <c>IAuditSink</c>
/// already established. <c>Biolog.Api.Modules.BioCrd006Module</c>'s own adapter resolves the
/// SAME singleton instance via constructor injection only, with no registration of its own.
///
/// Depends on <see cref="TenantModule"/> and <see cref="BioCrd005Module"/> (registered as
/// <c>"CardIssuance"</c>).
/// </summary>
public sealed class BioCrd007Module : IBiologModule
{
    public string Name => "CardProduction";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "CardIssuance"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddScoped<ICardIssueGateway>(
            sp => new CardIssueGatewayAdapter(sp.GetRequiredService<Biolog.Features.CardIssuance.CardIssueService>()));
        services.AddSingleton<Biolog.Api.Cards.CardActivationBarrierLockRegistry>();
        services.AddScoped<ICardActivationBarrier>(
            sp => new CardActivationBarrierAdapter(sp.GetRequiredService<Biolog.Api.Cards.CardActivationBarrierLockRegistry>()));

        services.AddSingleton<IProductionJobRepository, InMemoryProductionJobRepository>();
        services.AddSingleton<IProductionPackageStore, InMemoryProductionPackageStore>();
        services.AddSingleton<IProductionSigningKeyStore, InMemoryProductionSigningKeyStore>();
        services.AddSingleton<IProductionAuditSink, InMemoryProductionAuditSink>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<ProductionJobLockRegistry>();

        services.AddScoped<ProductionService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ProductionEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class CardIssueGatewayAdapter(Biolog.Features.CardIssuance.CardIssueService cardIssueService) : ICardIssueGateway
    {
        public async Task<CardIssueSnapshot?> GetAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
        {
            var issue = await Run(() => cardIssueService.GetAsync(personId, cardClass, sequenceNumber, cancellationToken));
            return issue is null ? null : ToSnapshot(issue);
        }

        public async Task<CardIssueSnapshot> ActivateAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, string idempotencyKey, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
        {
            var issue = await Run(() => cardIssueService.ActivateAsync(personId, cardClass, sequenceNumber, expectedVersion, idempotencyKey, ToTheirActor(actor), correlationId, cancellationToken));
            return ToSnapshot(issue);
        }

        private static async Task<T> Run<T>(Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (Biolog.Features.CardIssuance.Domain.CardIssueError error)
            {
                throw new ProductionError(error.Message, error.Code, error.Status, error.Detail);
            }
        }

        private static CardIssueSnapshot ToSnapshot(Biolog.Features.CardIssuance.Domain.CardIssue issue) => new()
        {
            PersonId = issue.PersonId,
            CardClass = issue.CardClass,
            SequenceNumber = issue.SequenceNumber,
            Status = issue.Status switch
            {
                Biolog.Features.CardIssuance.Domain.CardIssueStatus.Issued => CardIssueLifecycleStatus.Issued,
                Biolog.Features.CardIssuance.Domain.CardIssueStatus.Active => CardIssueLifecycleStatus.Active,
                Biolog.Features.CardIssuance.Domain.CardIssueStatus.Deactivated => CardIssueLifecycleStatus.Deactivated,
                _ => throw new ArgumentOutOfRangeException(nameof(issue), issue.Status, "unreachable: exhaustive switch"),
            },
            Version = issue.Version,
        };

        private static Biolog.Features.CardIssuance.Audit.AuditActor ToTheirActor(AuditActor actor) => new(
            actor.Type switch
            {
                ActorType.User => Biolog.Features.CardIssuance.Audit.ActorType.User,
                ActorType.ServiceAccount => Biolog.Features.CardIssuance.Audit.ActorType.ServiceAccount,
                ActorType.Device => Biolog.Features.CardIssuance.Audit.ActorType.Device,
                ActorType.System => Biolog.Features.CardIssuance.Audit.ActorType.System,
                ActorType.Anonymous => Biolog.Features.CardIssuance.Audit.ActorType.Anonymous,
                ActorType.Unknown => Biolog.Features.CardIssuance.Audit.ActorType.Unknown,
                _ => throw new ArgumentOutOfRangeException(nameof(actor), actor.Type, "unreachable: exhaustive switch"),
            },
            actor.Id);
    }

    /// <summary>BIO-OPS-003-AC04 round 2: a thin pass-through onto the SHARED <see cref="Biolog.Api.Cards.CardActivationBarrierLockRegistry"/> singleton - the same instance <c>Biolog.Api.Modules.BioCrd006Module</c>'s own adapter also wraps.</summary>
    private sealed class CardActivationBarrierAdapter(Biolog.Api.Cards.CardActivationBarrierLockRegistry registry) : ICardActivationBarrier
    {
        public Task<IDisposable> AcquireAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken) =>
            registry.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);
    }
}
