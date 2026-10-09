using Biolog.Api.Audit;
using Biolog.Api.Features.DeviceCertification;
using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;
using Biolog.Features.DeviceAdapters;
using Biolog.Features.DeviceOperations;
using Biolog.Features.DeviceOperations.Api;

namespace Biolog.Api.Modules;

/// <summary>
/// BIO-DEV-004's health-polling/heartbeat and remote-job framework, registered into this
/// host via the same thin-adapter shape every separate-project feature uses (see
/// <c>Biolog.Features.DeviceOperations.csproj</c>'s own comment for why the project itself
/// cannot reference <see cref="DeviceRegistry.DeviceService"/>,
/// <see cref="DeviceCertification.DeviceCertificationService"/> or BIO-DEV-003's own
/// <see cref="DeviceAdapterService"/>/<see cref="IDeviceAdapterRegistry"/> directly).
///
/// Endpoints live under the existing <c>/api/v1/devices</c> group (same group
/// <see cref="DeviceRegistryModule"/> and <see cref="BioDev003Module"/> already map into) -
/// a heartbeat poll and a remote job are both device sub-resource actions, not a new
/// top-level resource.
///
/// <b>SY-DEV004-01 (Seyi, round 1, Critical), fixed</b>: every route below now requires a
/// resolved, provenance-verified actor (<see cref="ActorResolver"/>/
/// <see cref="ActorProvenanceGate"/>, ADR-0006 Decision 3) who holds the relevant least-
/// privilege permission (<see cref="DeviceHealthPollingService.ReadPermission"/> for list/poll/
/// capabilities, <see cref="DeviceRemoteJobService.ExecutePermission"/> for actually running a
/// remote job - checked via BIO-IDN-006's own <c>PermissionService.ActorHasPermissionAsync</c>)
/// - the same seam <see cref="BioTrn003Module"/>/<see cref="BioInt004Module"/> already
/// established, now also applied here rather than diverging on the (rejected) reasoning that
/// this story's own dependency chain has none. The audit actor is derived ONLY from the
/// verified <c>X-Actor-Id</c>/<c>X-Actor-Provenance</c> headers now - the request body no
/// longer carries actor fields, closing the "forgeable audit actor" half of the same finding.
///
/// Depends on <see cref="DeviceRegistryModule"/>, <see cref="DeviceCertificationModule"/>,
/// <see cref="BioDev003Module"/> (for <see cref="DeviceAdapterService"/>/
/// <see cref="IDeviceAdapterRegistry"/>, via the adapters below), <see cref="BioIdn006Module"/>
/// (registered as <c>"Rbac"</c>) and <c>Tenant</c>.
/// </summary>
public sealed class BioDev004Module : IBiologModule
{
    public string Name => "DeviceOperations";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "DeviceRegistry", "DeviceCertification", "DeviceAdapters", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDeviceDirectoryGateway, DeviceDirectoryGatewayAdapter>();
        services.AddScoped<IDeviceHealthCheckGateway, DeviceHealthCheckGatewayAdapter>();
        services.AddScoped<IDeviceHeartbeatWriter, DeviceHeartbeatWriterAdapter>();
        services.AddScoped<IRemoteJobCapabilityGateway, RemoteJobCapabilityGatewayAdapter>();
        services.AddScoped<IRemoteJobAuditGateway, RemoteJobAuditGatewayAdapter>();
        services.AddSingleton<IRemoteJobExecutor, SimulatedRemoteJobExecutor>();

        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();

        services.AddScoped<DeviceHealthPollingService>();
        services.AddScoped<DeviceRemoteJobService>();
    }

    /// <summary>
    /// Same "seed data, not provisioning logic" shape <c>DemoSessionIdentitySeed</c>/
    /// <c>BioTrn003Module</c> already established: fail-closed/inert unless
    /// <c>WebSessionBridge:SyntheticTenantId</c> is configured, idempotent, writes directly to
    /// the repository (never through <c>PermissionService.SetGrantAsync</c>, which expects a
    /// real administrative actor and audit trail a fixed startup fixture has neither of).
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
        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.TenantAdmin, DeviceHealthPollingService.ReadPermission);
        SeedGrant(grants, tenantId, Biolog.Features.Rbac.Domain.RoleType.TenantAdmin, DeviceRemoteJobService.ExecutePermission);
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
        var group = endpoints.MapGroup("/api/v1/devices");

        group.MapGet("/", async (
            HttpContext context, DeviceHealthPollingService pollingService, ITenantContextAccessor tenantAccessor,
            ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            try
            {
                var tenantId = RequireTenant(tenantAccessor);
                var devices = await pollingService.ListDevicesAsync(tenantId, actor!, cancellationToken);
                return Results.Ok(devices);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
        });

        group.MapPost("/{deviceId}/poll-health", async (
            HttpContext context, string deviceId, DeviceHeartbeatRequest request, DeviceHealthPollingService pollingService,
            ITenantContextAccessor tenantAccessor, ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            try
            {
                var tenantId = RequireTenant(tenantAccessor);
                var result = await pollingService.PollHealthAsync(tenantId, deviceId, actor!, request.CorrelationId, cancellationToken);
                return Results.Ok(result.Device);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
            catch (DeviceNotFoundException)
            {
                return Results.NotFound();
            }
            catch (DeviceCombinationUnsupportedException ex)
            {
                return UnsupportedProblem(ex);
            }
            catch (DeviceAdapterUnavailableException ex)
            {
                return AdapterUnavailableProblem(ex);
            }
            catch (HeartbeatConcurrencyConflictException ex)
            {
                return Results.Problem(
                    title: "concurrency_conflict",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["current_version"] = ex.CurrentVersion });
            }
        });

        group.MapGet("/{deviceId}/remote-job-capabilities", async (
            HttpContext context, string deviceId, DeviceRemoteJobService jobService, ITenantContextAccessor tenantAccessor,
            ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            try
            {
                var tenantId = RequireTenant(tenantAccessor);
                var supported = await jobService.GetSupportedJobTypesAsync(tenantId, actor!, deviceId, cancellationToken);
                return Results.Ok(supported);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
            catch (DeviceNotFoundException)
            {
                return Results.NotFound();
            }
            catch (DeviceCombinationUnsupportedException ex)
            {
                return UnsupportedProblem(ex);
            }
            catch (DeviceAdapterUnavailableException ex)
            {
                return AdapterUnavailableProblem(ex);
            }
        });

        group.MapPost("/{deviceId}/remote-jobs", async (
            HttpContext context, string deviceId, RemoteJobRequest request, DeviceRemoteJobService jobService,
            ITenantContextAccessor tenantAccessor, ActorProvenanceGate provenanceGate, CancellationToken cancellationToken) =>
        {
            var (actor, actorProblem) = ResolveActor(context, provenanceGate);
            if (actorProblem is not null)
            {
                return actorProblem;
            }

            try
            {
                var tenantId = RequireTenant(tenantAccessor);
                var result = await jobService.RunJobAsync(tenantId, actor!, deviceId, request.JobType, request.CorrelationId, cancellationToken);
                return Results.Ok(result);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (PermissionRequiredException ex)
            {
                return PermissionProblem(ex);
            }
            catch (DeviceNotFoundException)
            {
                return Results.NotFound();
            }
            catch (DeviceCombinationUnsupportedException ex)
            {
                return UnsupportedProblem(ex);
            }
            catch (DeviceAdapterUnavailableException ex)
            {
                return AdapterUnavailableProblem(ex);
            }
            catch (RemoteJobNotSupportedException ex)
            {
                return Results.Problem(
                    title: "remote_job_not_supported",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["job_type"] = ex.JobType.ToString() });
            }
        });
    }

    private static (Biolog.Features.DeviceOperations.AuditActor? Actor, IResult? Problem) ResolveActor(HttpContext context, ActorProvenanceGate provenanceGate)
    {
        try
        {
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.ActorId);
            return (actor, null);
        }
        catch (ActorRequiredException ex)
        {
            return (null, Results.Problem(title: "actor_required", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }
        catch (InvalidActorTypeException ex)
        {
            return (null, Results.Problem(title: "invalid_actor_type", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }
        catch (ActorProvenanceRequiredException ex)
        {
            return (null, Results.Problem(title: "actor_provenance_required", detail: ex.Message, statusCode: StatusCodes.Status401Unauthorized));
        }
    }

    private static IResult UnsupportedProblem(DeviceCombinationUnsupportedException ex) => Results.Problem(
        title: "device_combination_unsupported",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?>
        {
            ["certification_status"] = ex.StatusLabel,
            ["certification_detail"] = ex.Detail,
        });

    private static IResult AdapterUnavailableProblem(DeviceAdapterUnavailableException ex) => Results.Problem(
        title: "device_adapter_unavailable",
        detail: ex.Message,
        statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult TenantContextProblem(TenantContextMissingException ex) => Results.Problem(
        title: "tenant_context_missing", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);

    private static IResult PermissionProblem(PermissionRequiredException ex) => Results.Problem(
        title: "permission_required",
        detail: ex.Message,
        statusCode: StatusCodes.Status403Forbidden,
        extensions: new Dictionary<string, object?> { ["permission_key"] = ex.PermissionKey });

    private static string RequireTenant(ITenantContextAccessor tenantAccessor) =>
        tenantAccessor.Context?.TenantId ?? throw new TenantContextMissingException();

    private static DeviceDescriptor ToDescriptor(DeviceSnapshot device) =>
        new(device.Vendor, device.Model, device.Firmware, device.Sdk, device.Serial);

    private static DeviceSnapshot ToSnapshot(Device device) => new(
        device.Id,
        device.TenantId,
        device.Vendor,
        device.Model,
        device.Firmware,
        device.Sdk,
        device.Serial,
        device.Status.ToString(),
        device.LastSeenAt,
        ToOperationsHealth(device.Health),
        device.HealthDetail,
        device.HealthCheckedAt,
        device.Version);

    private static Biolog.Features.DeviceOperations.DeviceHealthStatus? ToOperationsHealth(Biolog.Features.DeviceAdapters.DeviceHealthStatus? status) =>
        status switch
        {
            null => null,
            Biolog.Features.DeviceAdapters.DeviceHealthStatus.Online => Biolog.Features.DeviceOperations.DeviceHealthStatus.Online,
            Biolog.Features.DeviceAdapters.DeviceHealthStatus.Degraded => Biolog.Features.DeviceOperations.DeviceHealthStatus.Degraded,
            Biolog.Features.DeviceAdapters.DeviceHealthStatus.Offline => Biolog.Features.DeviceOperations.DeviceHealthStatus.Offline,
            Biolog.Features.DeviceAdapters.DeviceHealthStatus.Unknown => Biolog.Features.DeviceOperations.DeviceHealthStatus.Unknown,
            _ => Biolog.Features.DeviceOperations.DeviceHealthStatus.Unknown,
        };

    private static Biolog.Features.DeviceAdapters.DeviceHealthStatus ToAdaptersHealth(Biolog.Features.DeviceOperations.DeviceHealthStatus status) =>
        status switch
        {
            Biolog.Features.DeviceOperations.DeviceHealthStatus.Online => Biolog.Features.DeviceAdapters.DeviceHealthStatus.Online,
            Biolog.Features.DeviceOperations.DeviceHealthStatus.Degraded => Biolog.Features.DeviceAdapters.DeviceHealthStatus.Degraded,
            Biolog.Features.DeviceOperations.DeviceHealthStatus.Offline => Biolog.Features.DeviceAdapters.DeviceHealthStatus.Offline,
            _ => Biolog.Features.DeviceAdapters.DeviceHealthStatus.Unknown,
        };

    /// <summary>BIO-DEV-004-AC02: which <see cref="RemoteJobType"/>s each
    /// <see cref="DeviceCategory"/> supports "where vendor APIs permit" - a deliberately
    /// explicit, hand-maintained table, not derived from any vendor SDK (none of
    /// BIO-DEV-003's four simulated vendor SDKs expose a remote-admin call at all; see
    /// docs/features/bio-dev-004/README.md's own disclosure).</summary>
    private static readonly IReadOnlyDictionary<DeviceCategory, IReadOnlyList<RemoteJobType>> SupportedJobsByCategory =
        new Dictionary<DeviceCategory, IReadOnlyList<RemoteJobType>>
        {
            [DeviceCategory.FingerprintScanner] = [RemoteJobType.Reboot, RemoteJobType.UpdateFirmware],
            [DeviceCategory.FaceTerminal] = [RemoteJobType.Reboot, RemoteJobType.UpdateFirmware, RemoteJobType.PushConfiguration],
            [DeviceCategory.NfcUhfReader] = [RemoteJobType.Reboot],
            [DeviceCategory.CardPrinter] = [RemoteJobType.Reboot, RemoteJobType.PushConfiguration],
        };

    /// <summary>Wraps BIO-IDN-006's own already-registered <c>PermissionService</c> - the same pattern BIO-TRN-003's/BIO-INT-004's own <c>PermissionGatewayAdapter</c> already established.</summary>
    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }

    private sealed class DeviceDirectoryGatewayAdapter(DeviceService deviceService) : IDeviceDirectoryGateway
    {
        public async Task<DeviceSnapshot?> GetAsync(string deviceId, CancellationToken cancellationToken)
        {
            var device = await deviceService.GetAsync(deviceId, cancellationToken);
            return device is null ? null : ToSnapshot(device);
        }

        public async Task<IReadOnlyList<DeviceSnapshot>> ListAsync(CancellationToken cancellationToken)
        {
            var devices = await deviceService.ListAsync(cancellationToken);
            return devices.Select(ToSnapshot).ToArray();
        }
    }

    private sealed class DeviceHealthCheckGatewayAdapter(DeviceAdapterService adapterService) : IDeviceHealthCheckGateway
    {
        public async Task<DeviceHealthSnapshot> CheckHealthAsync(DeviceSnapshot device, CancellationToken cancellationToken)
        {
            try
            {
                var health = await adapterService.CheckHealthAsync(ToDescriptor(device), cancellationToken);
                return new DeviceHealthSnapshot(ToOperationsHealth(health.Status)!.Value, health.Detail, health.CheckedAt);
            }
            catch (UnsupportedDeviceCombinationException ex)
            {
                throw new DeviceCombinationUnsupportedException(
                    device.Vendor, device.Model, device.Firmware, device.Sdk, ex.StatusLabel, ex.Detail);
            }
            catch (DeviceAdapterNotAvailableException)
            {
                throw new DeviceAdapterUnavailableException(device.Vendor, device.Model);
            }
        }
    }

    private sealed class DeviceHeartbeatWriterAdapter(DeviceService deviceService) : IDeviceHeartbeatWriter
    {
        public async Task<DeviceSnapshot?> RecordHeartbeatAsync(
            string deviceId,
            Biolog.Features.DeviceOperations.DeviceHealthStatus health,
            string? detail,
            DateTimeOffset checkedAt,
            int expectedVersion,
            Biolog.Features.DeviceOperations.AuditActor actor,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            try
            {
                var updated = await deviceService.RecordHeartbeatAsync(
                    deviceId, ToAdaptersHealth(health), detail, checkedAt, expectedVersion,
                    new Biolog.Api.Audit.AuditActor(actor.ActorType, actor.ActorId), correlationId, cancellationToken);
                return updated is null ? null : ToSnapshot(updated);
            }
            catch (DeviceConcurrencyConflictException ex)
            {
                throw new HeartbeatConcurrencyConflictException(ex.CurrentVersion);
            }
        }
    }

    /// <summary>
    /// BIO-DEV-004-AC02/AC04's real bridge: certification first (same
    /// "<c>Unsupported</c> and no-entry-at-all both fail closed" rule
    /// <see cref="BioDev003Module.CertificationGateAdapter"/> already established), then
    /// adapter resolution (to learn the device's real <see cref="DeviceCategory"/>), then
    /// the explicit job-type support table.
    /// </summary>
    private sealed class RemoteJobCapabilityGatewayAdapter(
        DeviceCertificationService certificationService, IDeviceAdapterRegistry adapterRegistry) : IRemoteJobCapabilityGateway
    {
        public async Task<IReadOnlyList<RemoteJobType>> GetSupportedJobTypesAsync(DeviceSnapshot device, CancellationToken cancellationToken)
        {
            var key = new DeviceCertificationKey(device.Vendor, device.Model, device.Firmware, device.Sdk);
            var certification = await certificationService.GetEffectiveAsync(key, cancellationToken);

            if (certification is null)
            {
                throw new DeviceCombinationUnsupportedException(
                    device.Vendor, device.Model, device.Firmware, device.Sdk, "NoCertificationRecord", null);
            }

            if (certification.Status == CertificationStatus.Unsupported)
            {
                throw new DeviceCombinationUnsupportedException(
                    device.Vendor, device.Model, device.Firmware, device.Sdk,
                    certification.Status.ToString(), certification.TestEvidenceReference);
            }

            var descriptor = ToDescriptor(device);
            var adapter = adapterRegistry.Resolve(descriptor) ?? throw new DeviceAdapterUnavailableException(device.Vendor, device.Model);

            return SupportedJobsByCategory.TryGetValue(adapter.Category, out var supported) ? supported : [];
        }
    }

    private sealed class RemoteJobAuditGatewayAdapter(IAuditSink auditSink) : IRemoteJobAuditGateway
    {
        public Task RecordAsync(RemoteJobAuditRecord record, CancellationToken cancellationToken)
        {
            var auditEvent = new AuditEvent
            {
                EventId = Identifiers.CanonicalId.NewEventId(),
                SchemaVersion = 1,
                TenantId = record.TenantId,
                Actor = new Biolog.Api.Audit.AuditActor(record.ActorType, record.ActorId),
                Action = "device.remote_job_executed",
                Target = new AuditTarget("device", record.DeviceId),
                Outcome = AuditOutcome.Allowed,
                ReasonCode = record.JobType.ToString(),
                CorrelationId = Identifiers.CanonicalId.IsWellFormed(record.CorrelationId, "evt")
                    ? record.CorrelationId!
                    : Identifiers.CanonicalId.NewEventId(),
                CausationId = null,
                OccurredAt = record.OccurredAt,
                ReceivedAt = record.OccurredAt,
                Classification = "internal",
            };

            return auditSink.RecordAsync(auditEvent, cancellationToken);
        }
    }
}

public sealed record DeviceHeartbeatRequest(string? CorrelationId);

public sealed record RemoteJobRequest(RemoteJobType JobType, string? CorrelationId);
