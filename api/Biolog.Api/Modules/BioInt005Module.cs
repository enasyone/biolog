using Biolog.Api.Tenancy;
using Biolog.Features.ImportConflictReview;
using Biolog.Features.ImportConflictReview.Api;
using Biolog.Features.ImportConflictReview.Audit;
using Biolog.Features.ImportConflictReview.Domain;
using Biolog.Features.ImportConflictReview.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// External import conflict review (BIO-INT-005), registered the same way every sibling
/// identity-wave module wraps its feature project.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <see cref="PermissionGatewayAdapter"/> wraps BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly - this story's RBAC integration seam.
///
/// <see cref="ImportConflictGatewayAdapter"/> wraps BIO-INT-002's own already-registered,
/// Scoped <c>ImportBatchService.ListBatchesAsync</c> directly - reachable here only because that
/// project's own csproj grants this assembly internals visibility, the same cross-story gateway
/// pattern every sibling story uses. Flattens every applied batch's own
/// <c>ImportRowOutcome</c>s into this feature's own local <see cref="ImportRowSnapshot"/> shape -
/// BIO-INT-002's own types never cross this boundary.
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioIdn006Module"/> (registered as
/// <c>"Rbac"</c>) and <see cref="BioInt002Module"/> (registered as <c>"Integrations"</c>).
/// </summary>
public sealed class BioInt005Module : IBiologModule
{
    public string Name => "ImportConflictReview";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "Rbac", "Integrations"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<IImportConflictReviewRepository, InMemoryImportConflictReviewRepository>();
        services.AddSingleton<IImportConflictReviewAuditSink, InMemoryImportConflictReviewAuditSink>();
        services.AddSingleton<ImportConflictReviewLockRegistry>();

        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));
        services.AddScoped<IImportConflictGateway>(
            sp => new ImportConflictGatewayAdapter(sp.GetRequiredService<Biolog.Features.Integrations.ImportBatchService>()));

        services.AddScoped<ImportConflictReviewService>();

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ImportConflictReviewEndpoints.Map(endpoints);
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

    private sealed class ImportConflictGatewayAdapter(Biolog.Features.Integrations.ImportBatchService importBatchService) : IImportConflictGateway
    {
        public async Task<IReadOnlyList<ImportRowSnapshot>> ListRowOutcomesAsync(string mappingId, CancellationToken cancellationToken = default)
        {
            var batches = await importBatchService.ListBatchesAsync(mappingId, cancellationToken);
            return
            [
                .. batches.SelectMany(batch => batch.RowOutcomes.Select(row => new ImportRowSnapshot
                {
                    ExternalKey = row.ExternalKey,
                    Outcome = Translate(row.Outcome),
                    PersonId = row.PersonId,
                    Conflicts = [.. row.Conflicts.Select(c => new ImportFieldConflict(c.PersonField, c.ExistingValue, c.IncomingValue))],
                    BatchId = batch.Id,
                    SubmittedAt = batch.SubmittedAt,
                })),
            ];
        }

        private static RowOutcomeSnapshot Translate(Biolog.Features.Integrations.Domain.RowOutcome outcome) => outcome switch
        {
            Biolog.Features.Integrations.Domain.RowOutcome.Created => RowOutcomeSnapshot.Created,
            Biolog.Features.Integrations.Domain.RowOutcome.Unchanged => RowOutcomeSnapshot.Unchanged,
            Biolog.Features.Integrations.Domain.RowOutcome.Conflict => RowOutcomeSnapshot.Conflict,
            Biolog.Features.Integrations.Domain.RowOutcome.Rejected => RowOutcomeSnapshot.Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unreachable: exhaustive switch"),
        };
    }
}
