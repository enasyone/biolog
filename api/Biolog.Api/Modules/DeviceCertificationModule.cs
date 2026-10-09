using Biolog.Api.Audit;
using Biolog.Api.Features.DeviceCertification;
using Biolog.Api.Features.DeviceCertification.Audit;
using Biolog.Api.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// The device certification matrix (BIO-DEV-002): supported/certified/experimental/
/// deprecated/unsupported statuses for device+SDK+firmware combinations. Two endpoint
/// groups expose the two scopes <see cref="DeviceCertificationService"/> implements:
///
/// - <c>/api/v1/device-certifications</c> - the platform-wide default matrix
///   ("As platform operations" per the user story), control-plane, no tenant context
///   required - mirrors <see cref="BioTen001Module"/>'s own control-plane shape.
/// - <c>/api/v1/tenant/device-certifications</c> - a tenant's own override
///   (BIO-DEV-002-AC03), fails closed on a missing tenant context exactly like every
///   <see cref="DeviceRegistryModule"/> endpoint (ADR-0002 Decision 2).
///
/// Depends on <see cref="TenantModule"/> so the tenant-scoped group reads whatever the
/// tenant middleware hook resolved; the control-plane group does not consult it at all
/// for its own reads/writes (only <see cref="DeviceCertificationService.GetEffectiveAsync"/>
/// optionally does, and only to prefer an override when one happens to be resolved).
///
/// Registers its own <see cref="ICertificationAuditSink"/>/<see cref="IDeviceCertificationRepository"/>
/// singletons rather than reusing <see cref="DeviceRegistryModule"/>'s <see cref="IAuditSink"/>:
/// ASP.NET Core's DI resolves a single-service registration to whichever module registered
/// it LAST, so a second <c>AddSingleton&lt;IAuditSink, ...&gt;</c> here would silently
/// shadow that module's own registration instead of adding a second one - a dedicated
/// interface per feature (the same choice BIO-TEN-001/BIO-TEN-002 already made for their
/// own audit sinks) avoids that collision entirely.
/// </summary>
public sealed class DeviceCertificationModule : IBiologModule
{
    public string Name => "DeviceCertification";
    public IReadOnlyCollection<string> DependsOn => new[] { "Tenant" };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDeviceCertificationRepository, InMemoryDeviceCertificationRepository>();
        services.AddSingleton<ICertificationAuditSink, InMemoryCertificationAuditSink>();
        services.AddSingleton<DeviceCertificationLockRegistry>();
        services.AddScoped<DeviceCertificationService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapGlobalEndpoints(endpoints.MapGroup("/api/v1/device-certifications"));
        MapTenantEndpoints(endpoints.MapGroup("/api/v1/tenant/device-certifications"));
    }

    private static void MapGlobalEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            DeviceCertificationRegistrationRequest request, DeviceCertificationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var entry = await service.RegisterGlobalAsync(request, cancellationToken);
                return Results.Created(LookupPath(entry), entry);
            }
            catch (InvalidDeviceCertificationKeyException ex)
            {
                return KeyProblem(ex);
            }
            catch (InvalidTestEvidenceReferenceException ex)
            {
                return EvidenceProblem(ex);
            }
            catch (DeviceCertificationAlreadyExistsException ex)
            {
                return AlreadyExistsProblem(ex);
            }
            catch (AuditUnavailableException ex)
            {
                return AuditProblem(ex);
            }
        });

        group.MapGet("/", async (string? status, DeviceCertificationService service, CancellationToken cancellationToken) =>
        {
            var entries = await service.ListGlobalAsync(cancellationToken);
            return Results.Ok(Filter(entries, status));
        });

        group.MapGet("/lookup", async (
            string vendor, string model, string firmware, string sdk, DeviceCertificationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var entry = await service.GetEffectiveAsync(new DeviceCertificationKey(vendor, model, firmware, sdk), cancellationToken);
                return entry is null ? Results.NotFound() : Results.Ok(entry);
            }
            catch (InvalidDeviceCertificationKeyException ex)
            {
                return KeyProblem(ex);
            }
        });

        group.MapPatch("/status", async (
            DeviceCertificationStatusChangeRequest request, DeviceCertificationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await service.UpdateGlobalAsync(request, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (InvalidDeviceCertificationKeyException ex)
            {
                return KeyProblem(ex);
            }
            catch (InvalidTestEvidenceReferenceException ex)
            {
                return EvidenceProblem(ex);
            }
            catch (DeviceCertificationConcurrencyConflictException ex)
            {
                return ConflictProblem(ex);
            }
            catch (AuditUnavailableException ex)
            {
                return AuditProblem(ex);
            }
        });
    }

    private static void MapTenantEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/", async (
            DeviceCertificationRegistrationRequest request, DeviceCertificationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var entry = await service.RegisterTenantOverrideAsync(request, cancellationToken);
                return Results.Created("/api/v1/tenant/device-certifications", entry);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (InvalidDeviceCertificationKeyException ex)
            {
                return KeyProblem(ex);
            }
            catch (InvalidTestEvidenceReferenceException ex)
            {
                return EvidenceProblem(ex);
            }
            catch (DeviceCertificationAlreadyExistsException ex)
            {
                return AlreadyExistsProblem(ex);
            }
            catch (AuditUnavailableException ex)
            {
                return AuditProblem(ex);
            }
        });

        group.MapGet("/", async (string? status, DeviceCertificationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var entries = await service.ListTenantOverridesAsync(cancellationToken);
                return Results.Ok(Filter(entries, status));
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
        });

        group.MapPatch("/status", async (
            DeviceCertificationStatusChangeRequest request, DeviceCertificationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await service.UpdateTenantOverrideAsync(request, cancellationToken);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (TenantContextMissingException ex)
            {
                return TenantContextProblem(ex);
            }
            catch (InvalidDeviceCertificationKeyException ex)
            {
                return KeyProblem(ex);
            }
            catch (InvalidTestEvidenceReferenceException ex)
            {
                return EvidenceProblem(ex);
            }
            catch (DeviceCertificationConcurrencyConflictException ex)
            {
                return ConflictProblem(ex);
            }
            catch (AuditUnavailableException ex)
            {
                return AuditProblem(ex);
            }
        });
    }

    private static string LookupPath(DeviceCertification entry) =>
        $"/api/v1/device-certifications/lookup?vendor={Uri.EscapeDataString(entry.Vendor)}&model={Uri.EscapeDataString(entry.Model)}" +
        $"&firmware={Uri.EscapeDataString(entry.Firmware)}&sdk={Uri.EscapeDataString(entry.Sdk)}";

    private static IReadOnlyList<DeviceCertification> Filter(IReadOnlyList<DeviceCertification> entries, string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return entries;
        }

        return Enum.TryParse<CertificationStatus>(status, ignoreCase: true, out var parsed)
            ? entries.Where(e => e.Status == parsed).ToArray()
            : Array.Empty<DeviceCertification>();
    }

    private static IResult KeyProblem(InvalidDeviceCertificationKeyException ex) => Results.Problem(
        title: "invalid_device_certification_key", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);

    private static IResult EvidenceProblem(InvalidTestEvidenceReferenceException ex) => Results.Problem(
        title: "invalid_test_evidence_reference", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);

    private static IResult AlreadyExistsProblem(DeviceCertificationAlreadyExistsException ex) => Results.Problem(
        title: "device_certification_already_exists", detail: ex.Message, statusCode: StatusCodes.Status409Conflict);

    private static IResult ConflictProblem(DeviceCertificationConcurrencyConflictException ex) => Results.Problem(
        title: "concurrency_conflict",
        detail: ex.Message,
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["current_version"] = ex.CurrentVersion });

    private static IResult AuditProblem(AuditUnavailableException ex) => Results.Problem(
        title: "audit_unavailable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult TenantContextProblem(TenantContextMissingException ex) => Results.Problem(
        title: "tenant_context_missing", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
}
