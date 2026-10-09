using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;
using Biolog.Features.CardLifecycle;
using Biolog.Features.CardLifecycle.Api;
using Biolog.Features.CardLifecycle.Audit;
using Biolog.Features.CardLifecycle.Domain;
using Biolog.Features.CardLifecycle.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Report lost/missing/stolen/damaged and card replacement (BIO-CRD-006), registered the same
/// way every sibling card/identity module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start, so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <b><see cref="CardIssueGatewayAdapter"/> is the real cross-story dependency.</b> It wraps
/// BIO-CRD-005's own already-registered, internal <c>CardIssueService</c> directly - reachable
/// here only because that project's own csproj grants <c>InternalsVisibleTo Biolog.Api</c>, the
/// same seam <see cref="BioCrd005Module"/>'s own <c>PersonExistenceGatewayAdapter</c> already
/// established for its dependency on BIO-IDN-001. Every call is wrapped in a translation of
/// BIO-CRD-005's own <c>CardIssueError</c> into this feature's own <see cref="CardLifecycleError"/>
/// (same code/status/detail) so <c>CardLifecycleEndpoints</c>'s error handling stays exhaustive
/// over exactly one exception type, never leaking a sibling feature's own domain exception
/// across the gateway boundary.
///
/// <b>Dependency: BIO-CRD-005 is a genuine runtime dependency, not sequencing.</b> Unlike
/// BIO-CRD-005's own "BIO-CRD-002 is sequencing, not coupling" disclosure, this story's declared
/// dependency on BIO-CRD-005 IS a real, load-bearing runtime call - reporting, replacing and
/// activate-replacement all read and mutate BIO-CRD-005's own card issue rows through this
/// adapter.
///
/// <b>ADR-0006 Decision 1/2/4.</b> This module wires no authorization/provenance gate - see
/// <see cref="Api.ActorResolver"/>'s header for why that is the correct, disclosed shape here.
///
/// <b>TD-CRD-006-DENY-LIST-DEVICE-AUTH (ADR-0002 Decision 7).</b> GET /deny-list shipped with no
/// auth of any kind. <see cref="DeviceExistenceGatewayAdapter"/> wraps BIO-DEV-001's own
/// already-registered <c>DeviceService.GetAsync</c>, requiring <see cref="DeviceStatus.Active"/>
/// - the same tenant-scoping-for-free reasoning <c>BioPho002Module</c>'s own identically-named
/// adapter already documents, which is also how this fix preserves tenant binding (see
/// <see cref="Domain.IDeviceExistenceGateway"/>'s own header). Device credential provisioning is
/// loaded from <see cref="IConfiguration"/> here, the same out-of-band path
/// <c>DeviceCredentialProvisioning</c> (BIO-PHO-002) established, under this feature's own
/// configuration section so the two features' device secrets are independent.
///
/// <b>BIO-OPS-003-AC04 round 1 (Kemi's architectural ruling).</b> <see cref="ProductionStatusGatewayAdapter"/>
/// wraps BIO-CRD-007's own already-registered <c>ProductionService.ListJobsAsync</c> directly -
/// the same shape <see cref="CardIssueGatewayAdapter"/> already established for BIO-CRD-005.
/// Reachable here only because that project's own csproj grants
/// <c>InternalsVisibleTo Biolog.Api</c>, same seam as every other cross-feature gateway adapter
/// in this module. BIO-CRD-007 itself needed zero changes - <c>ListJobsAsync</c> was already
/// public.
///
/// <b>BIO-OPS-003-AC04 round 2 (SY-OPS003-02, Kemi's ruling).</b> <see cref="CardActivationBarrierAdapter"/>
/// wraps the new, SHARED, host-layer <c>Biolog.Api.Cards.CardActivationBarrierLockRegistry</c> -
/// the same singleton instance <c>Biolog.Api.Modules.BioCrd007Module</c>'s own adapter also
/// wraps, for genuine cross-feature serialization (see <see cref="Domain.ICardActivationBarrier"/>'s
/// own header). Registered ONCE, in <c>BioCrd007Module</c> (Emeka's own suggestion, matching the
/// key-granularity precedent - that module already owns the finer-grained
/// <c>(tenant, person, cardClass, sequenceNumber)</c> key this shared lock reuses) - this module
/// only resolves it via constructor injection, the same "register once, consume elsewhere" shape
/// <c>DeviceRegistryModule</c>'s own <c>IAuditSink</c> already established. The already-existing
/// <c>DependsOn</c> edge on <c>"CardProduction"</c> (added round 1) already guarantees
/// registration order is never actually load-bearing here either way, since DI registration
/// itself is deferred until the whole service collection is built, but keeping ownership in the
/// one module avoids a second, redundant registration of a stateful singleton.
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioCrd005Module"/> (registered as
/// <c>"CardIssuance"</c>), <see cref="BioCrd007Module"/> (registered as <c>"CardProduction"</c>,
/// added by this fix) and <see cref="DeviceRegistryModule"/>.
/// </summary>
public sealed class BioCrd006Module : IBiologModule
{
    public string Name => "CardLifecycle";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "CardIssuance", "CardProduction", "DeviceRegistry"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddScoped<ICardIssueGateway>(
            sp => new CardIssueGatewayAdapter(sp.GetRequiredService<Biolog.Features.CardIssuance.CardIssueService>()));
        services.AddScoped<IProductionStatusGateway>(
            sp => new ProductionStatusGatewayAdapter(sp.GetRequiredService<Biolog.Features.CardProduction.ProductionService>()));
        services.AddScoped<ICardActivationBarrier>(
            sp => new CardActivationBarrierAdapter(sp.GetRequiredService<Biolog.Api.Cards.CardActivationBarrierLockRegistry>()));
        services.AddScoped<IDeviceExistenceGateway>(
            sp => new DeviceExistenceGatewayAdapter(sp.GetRequiredService<DeviceService>()));

        services.AddSingleton(_ => DeviceCredentialProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<IDeviceCredentialStore>(sp => sp.GetRequiredService<InMemoryDeviceCredentialStore>());
        services.AddScoped<IDeviceAuthenticator, HeaderDeviceAuthenticator>();

        services.AddSingleton<ICardLifecycleRepository, InMemoryCardLifecycleRepository>();
        services.AddSingleton<ICardRecoveryPolicyStore, InMemoryCardRecoveryPolicyStore>();
        services.AddSingleton<ICardLifecycleAuditSink, InMemoryCardLifecycleAuditSink>();
        services.AddSingleton<IPresentationEventLog, InMemoryPresentationEventLog>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
        services.AddSingleton<CardLifecycleLockRegistry>();

        services.AddScoped<CardLifecycleService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        CardLifecycleEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class DeviceExistenceGatewayAdapter(DeviceService deviceService) : IDeviceExistenceGateway
    {
        public async Task<bool> IsActiveAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            var device = await deviceService.GetAsync(deviceId, cancellationToken);
            return device is not null && device.Status == DeviceStatus.Active;
        }
    }

    private sealed class CardIssueGatewayAdapter(Biolog.Features.CardIssuance.CardIssueService cardIssueService) : ICardIssueGateway
    {
        public async Task<CardIssueSnapshot?> GetAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
        {
            var issue = await Run(() => cardIssueService.GetAsync(personId, cardClass, sequenceNumber, cancellationToken));
            return issue is null ? null : ToSnapshot(issue);
        }

        public async Task<IReadOnlyList<CardIssueSnapshot>> ListAsync(string personId, string cardClass, CancellationToken cancellationToken = default)
        {
            var issues = await Run(() => cardIssueService.ListAsync(personId, cardClass, cancellationToken));
            return [.. issues.Select(ToSnapshot)];
        }

        public async Task<CardIssueSnapshot> IssueReplacementAsync(string personId, string cardClass, ReplacementCardRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
        {
            var issueRequest = new Biolog.Features.CardIssuance.IssueCardRequest
            {
                TemplateRef = request.TemplateRef,
                PhotoRef = request.PhotoRef,
                LogoRef = request.LogoRef,
                SignatureRef = request.SignatureRef,
                PrinterRef = request.PrinterRef,
            };
            var issue = await Run(() => cardIssueService.IssueAsync(personId, cardClass, issueRequest, ToTheirActor(actor), correlationId, cancellationToken));
            return ToSnapshot(issue);
        }

        public async Task<CardIssueSnapshot> ActivateAsync(string personId, string cardClass, int sequenceNumber, int? expectedVersion, string idempotencyKey, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
        {
            var issue = await Run(() => cardIssueService.ActivateAsync(personId, cardClass, sequenceNumber, expectedVersion, idempotencyKey, ToTheirActor(actor), correlationId, cancellationToken));
            return ToSnapshot(issue);
        }

        public async Task<CardIssueSnapshot> DeactivateAsync(string personId, string cardClass, int sequenceNumber, int expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
        {
            var issue = await Run(() => cardIssueService.DeactivateAsync(personId, cardClass, sequenceNumber, expectedVersion, ToTheirActor(actor), correlationId, cancellationToken));
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
                throw new CardLifecycleError(error.Message, error.Code, error.Status, error.Detail);
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

    /// <summary>BIO-OPS-003-AC04: wraps BIO-CRD-007's own already-registered <c>ProductionService.ListJobsAsync</c> - the latest job by <c>JobSequenceNumber</c> decides whether activation is allowed.</summary>
    private sealed class ProductionStatusGatewayAdapter(Biolog.Features.CardProduction.ProductionService productionService) : IProductionStatusGateway
    {
        public async Task<ProductionStatusSnapshot> HasPassedAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
        {
            var jobs = await productionService.ListJobsAsync(personId, cardClass, sequenceNumber, cancellationToken);
            var latest = jobs.OrderByDescending(j => j.JobSequenceNumber).FirstOrDefault();
            return new ProductionStatusSnapshot
            {
                HasPassed = latest is not null && Biolog.Features.CardProduction.Domain.ProductionJobStatusExtensions.AllowsActivation(latest.Status),
                LatestJobSequenceNumber = latest?.JobSequenceNumber,
                LatestStatus = latest is null ? null : ToMirror(latest.Status),
            };
        }

        private static ProductionJobLifecycleStatus ToMirror(Biolog.Features.CardProduction.Domain.ProductionJobStatus status) => status switch
        {
            Biolog.Features.CardProduction.Domain.ProductionJobStatus.InProgress => ProductionJobLifecycleStatus.InProgress,
            Biolog.Features.CardProduction.Domain.ProductionJobStatus.Passed => ProductionJobLifecycleStatus.Passed,
            Biolog.Features.CardProduction.Domain.ProductionJobStatus.Failed => ProductionJobLifecycleStatus.Failed,
            Biolog.Features.CardProduction.Domain.ProductionJobStatus.Rejected => ProductionJobLifecycleStatus.Rejected,
            Biolog.Features.CardProduction.Domain.ProductionJobStatus.Cancelled => ProductionJobLifecycleStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
        };
    }

    /// <summary>BIO-OPS-003-AC04 round 2: a thin pass-through onto the SHARED <see cref="Biolog.Api.Cards.CardActivationBarrierLockRegistry"/> singleton - never this feature's own state.</summary>
    private sealed class CardActivationBarrierAdapter(Biolog.Api.Cards.CardActivationBarrierLockRegistry registry) : ICardActivationBarrier
    {
        public Task<IDisposable> AcquireAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken) =>
            registry.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);
    }
}
