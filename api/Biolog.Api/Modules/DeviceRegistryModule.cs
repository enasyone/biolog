using Biolog.Api.Audit;
using Biolog.Api.Features.DeviceRegistry;
using Biolog.Api.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// The device registry (BIO-DEV-001): readers, terminals, scanners, printers and edge
/// devices, registered before any vendor adapter exists. Endpoints live under
/// <c>/api/v1/devices</c> per ADR-0004 §1.1's path versioning; this module does not
/// otherwise implement ADR-0004's idempotency-key header or its fully validated
/// problem+json builder (Decision 2.5) - neither is named by any of this story's four ACs,
/// and pulling in a partial version of a shared, general-purpose HTTP contract that no
/// other .NET endpoint yet exercises risked producing a second, competing implementation
/// of it later. ASP.NET Core's own <see cref="Results.Problem"/> is used instead, which
/// already produces a reasonable <c>application/problem+json</c> body.
///
/// Depends on <see cref="TenantModule"/> so every device endpoint reads whatever tenant
/// context that hook resolved (today, always <c>null</c> - see
/// <see cref="Tenancy.TenantContextMissingException"/>).
///
/// Also registers <see cref="ICrossTenantDeviceLookup"/> (Kemi's architectural ruling on
/// BIO-MOB-003's acceptance FAIL, 2026-09-23): a narrow, internal exception to this feature's
/// otherwise-universal "every method needs an ambient tenant" rule, added for a sibling
/// feature (BIO-MOB-003's central sync intake) that has no ambient tenant channel and must
/// resolve one FROM an authenticated device instead. See that interface's own doc for the
/// full scope and safety reasoning.
/// </summary>
public sealed class DeviceRegistryModule : IBiologModule
{
    public string Name => "DeviceRegistry";
    public IReadOnlyCollection<string> DependsOn => new[] { "Tenant" };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // BIO-MOB-003's device-authenticated tenant resolution (Kemi's ruling, 2026-09-23)
        // needs the same InMemoryDeviceRepository instance behind both its ordinary,
        // tenant-scoped IDeviceRepository surface and the narrow, internal
        // ICrossTenantDeviceLookup escape hatch - registered as a singleton concrete type
        // once, then bound to both interfaces, so there is exactly one device store.
        services.AddSingleton<InMemoryDeviceRepository>();
        services.AddSingleton<IDeviceRepository>(sp => sp.GetRequiredService<InMemoryDeviceRepository>());
        services.AddSingleton<ICrossTenantDeviceLookup>(sp => sp.GetRequiredService<InMemoryDeviceRepository>());
        services.AddSingleton<IAuditSink, InMemoryAuditSink>();
        services.AddSingleton<DeviceLockRegistry>();
        services.AddScoped<DeviceService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/devices");

        group.MapPost("/", async (DeviceRegistrationRequest request, DeviceService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var device = await service.RegisterAsync(request, cancellationToken);
                return Results.Created($"/api/v1/devices/{device.Id}", device);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (InvalidCredentialReferenceException ex)
            {
                return Results.Problem(
                    title: "invalid_credential_reference",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        group.MapGet("/{deviceId}", async (string deviceId, DeviceService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var device = await service.GetAsync(deviceId, cancellationToken);
                return device is null
                    ? Results.NotFound()
                    : Results.Ok(device);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
        });

        group.MapPatch("/{deviceId}/status", async (
            string deviceId, DeviceStatusChangeRequest request, DeviceService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var actor = new AuditActor(request.ActorType, request.ActorId);
                var updated = await service.ChangeStatusAsync(
                    deviceId, request.Status, request.ExpectedVersion, actor, request.CorrelationId, cancellationToken);
                return updated is null
                    ? Results.NotFound()
                    : Results.Ok(updated);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (DeviceConcurrencyConflictException ex)
            {
                return Results.Problem(
                    title: "concurrency_conflict",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["current_version"] = ex.CurrentVersion });
            }
            catch (AuditUnavailableException ex)
            {
                return Results.Problem(
                    title: "audit_unavailable",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }

    private static IResult TenantContextProblem(TenantContextMissingException ex) => Results.Problem(
        title: "tenant_context_missing",
        detail: ex.Message,
        statusCode: StatusCodes.Status400BadRequest);
}
