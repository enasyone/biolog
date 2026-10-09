using Biolog.Api.Tenancy;
using Biolog.Features.Attendance.Domain;
using Biolog.Features.CardProduction.Domain;
using Biolog.Features.ReportBuilder;
using Biolog.Features.ReportBuilder.Api;
using Biolog.Features.ReportBuilder.Audit;
using Biolog.Features.ReportBuilder.Domain;
using Biolog.Features.ReportBuilder.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Governed report builder foundation (BIO-RPT-002), registered the same way every sibling
/// module wraps its feature project - written directly here, in <c>Biolog.Api.Modules</c>, from
/// the start, so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <b><see cref="CardProductionGatewayAdapter"/> and <see cref="AttendanceGatewayAdapter"/> are
/// the real cross-story dependencies.</b> Unlike most prior gateway adapters in this epic, both
/// wrap a PUBLIC repository interface the dependency's own module already registers
/// (<c>IProductionJobRepository</c>, <c>IAttendanceEventRepository</c>) rather than an internal
/// application service - no <c>InternalsVisibleTo</c> grant from either dependency's own csproj
/// is needed for that reason. <c>IProductionJobRepository.ListForTenant</c> is a new, purely
/// additive method this story added to BIO-CRD-007's own repository interface (that story's own
/// original scope never needed a tenant-wide read; every existing method/behaviour there is
/// unchanged) - see that interface's own doc comment.
///
/// Depends on <see cref="TenantModule"/>, <see cref="BioCrd007Module"/> (registered as
/// <c>"CardProduction"</c>) and <see cref="BioAtt001Module"/> (registered as <c>"Attendance"</c>).
/// </summary>
public sealed class BioRpt002Module : IBiologModule
{
    public string Name => "ReportBuilder";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "CardProduction", "Attendance"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        // Singleton, not Scoped like most gateway adapters in this epic: unlike
        // ICardIssueGateway/etc., neither gateway here resolves anything from a scoped
        // accessor - tenantId flows as an explicit ListAsync parameter, and both repositories
        // they wrap are themselves Singletons. ReportJobRunner (below) is also a genuine
        // process-lifetime Singleton (BIO-RPT-002-AC04's own cross-request cancellation
        // registry must survive past any one HTTP request's scope), so every service on its
        // own dependency chain must be Singleton too - ASP.NET Core's own DI validation
        // refuses a Singleton capturing a Scoped service, caught here at host startup rather
        // than only at first-request time.
        services.AddSingleton<ICardProductionGateway>(
            sp => new CardProductionGatewayAdapter(sp.GetRequiredService<Biolog.Features.CardProduction.Domain.IProductionJobRepository>()));
        services.AddSingleton<IAttendanceGateway>(
            sp => new AttendanceGatewayAdapter(sp.GetRequiredService<Biolog.Features.Attendance.Domain.IAttendanceEventRepository>()));

        services.AddSingleton<IReportJobRepository, InMemoryReportJobRepository>();
        services.AddSingleton<IReportAuditSink, InMemoryReportAuditSink>();

        services.AddSingleton<ReportGenerationService>();
        services.AddSingleton<ReportJobRunner>();
        services.AddScoped<ReportService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ReportEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class CardProductionGatewayAdapter(Biolog.Features.CardProduction.Domain.IProductionJobRepository repository) : ICardProductionGateway
    {
        public Task<IReadOnlyList<CardProductionRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<CardProductionRecord> records =
                [.. repository.ListForTenant(tenantId).Select(j => new CardProductionRecord(j.Status.ToWireString(), j.Source, j.CardClass))];
            return Task.FromResult(records);
        }
    }

    private sealed class AttendanceGatewayAdapter(Biolog.Features.Attendance.Domain.IAttendanceEventRepository repository) : IAttendanceGateway
    {
        public async Task<IReadOnlyList<AttendanceRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            var events = await repository.ListForTenantAsync(tenantId, cancellationToken);
            return [.. events.Select(e => new AttendanceRecord(e.Mode.ToWireString(), e.Source.ToWireString(), e.Evidence.Method.ToWireString()))];
        }
    }
}
