using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FaceSearch.Audit;
using Biolog.Features.FaceSearch.Domain;
using Biolog.Features.FaceSearch.Service;
using Biolog.Features.TenantFeatureConfig.Integration;

namespace Biolog.Api.Modules;

/// <summary>
/// Registers BIO-BIO-007's 1:N face identification/duplicate search into the host
/// (BIO-FND-008-AC05 extension seam), the same thin-adapter shape every prior BIO-BIO module
/// established: this module lives here, in <c>Biolog.Api.Modules</c>, never inside
/// <c>api/features/bio-bio-007/**</c>, so the dependency between <c>Biolog.Api</c> and the
/// feature project stays one-way.
///
/// <para>
/// This module resolves BIO-BIO-001's own already-registered <see cref="IFaceProvider"/>/
/// <see cref="IBiometricMatcher"/> singletons rather than registering second copies - there is
/// exactly one of each in the host, reused by every story that needs them.
/// </para>
///
/// <para>
/// <b>BIO-BIO-007-QA-01 (Chioma acceptance, High).</b> <see cref="FaceGalleryApprovalGatewayAdapter"/>
/// wraps BIO-BIO-003's own already-registered <c>FaceEnrolmentService.GetLatestReferenceAsync</c>
/// directly - the real proof of "approved" the user story's own wording requires (see the
/// feature project's own <c>Domain/IFaceGalleryApprovalGateway.cs</c> and
/// <c>Service/FaceSearchService.cs</c> remarks for the full finding and fix).
/// <c>FaceEnrolmentService</c> is Scoped (it depends on a Scoped photo-master gateway), so
/// <see cref="FaceSearchService"/> is now registered Scoped too - a Singleton cannot safely
/// capture a Scoped dependency (captive-dependency avoidance, the same reasoning every prior
/// BIO-BIO module documents for its own service).
/// </para>
///
/// <para>
/// <see cref="Domain.IFaceGalleryStore"/>, <see cref="Domain.IFaceSearchFeatureFlagPolicy"/>,
/// <see cref="Audit.IFaceSearchAuditSink"/>, their <c>InMemory</c>/<c>Synthetic</c>
/// implementations and <see cref="FaceSearchService"/> are all reachable here only because of
/// the feature project's explicit <c>InternalsVisibleTo</c> grant (BIO-IDN-001-QA-01's
/// governance shape). BIO-TEN-003 (Tunde) swaps the <see cref="Domain.IFaceSearchFeatureFlagPolicy"/>
/// registration below from the throwaway <c>SyntheticFaceSearchFeatureFlagPolicy</c> to a real,
/// persisted-in-process adapter - this interface's own doc comment explicitly invited exactly
/// that later story; see docs/features/bio-ten-003/README.md for the full, disclosed account.
/// </para>
///
/// <para>
/// No HTTP endpoints are mapped by this module, the same deferred-integration judgment call
/// every prior BIO-BIO module made: AC01-AC05 are about the search/population/audit MODEL, and
/// the service takes a caller-resolved tenant/person/actor id rather than resolving one itself,
/// so a later story can map real endpoints onto <see cref="FaceSearchService"/> without this
/// feature's shape changing.
/// </para>
/// </summary>
public sealed class BioBio007Module : IBiologModule
{
    public string Name => "FaceSearch";
    public IReadOnlyCollection<string> DependsOn => ["Biometrics", "FaceEnrolment"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFaceGalleryStore, InMemoryFaceGalleryStore>();
        services.AddSingleton<IFaceSearchFeatureFlagPolicy, TenantFeatureFlagPolicyAdapter>();
        services.AddSingleton<IFaceSearchAuditSink, InMemoryFaceSearchAuditSink>();
        services.AddScoped<IFaceGalleryApprovalGateway>(sp => new FaceGalleryApprovalGatewayAdapter(
            sp.GetRequiredService<Biolog.Features.FaceEnrolment.Service.FaceEnrolmentService>()));

        services.AddScoped<FaceSearchService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }

    private sealed class FaceGalleryApprovalGatewayAdapter(Biolog.Features.FaceEnrolment.Service.FaceEnrolmentService faceEnrolmentService)
        : IFaceGalleryApprovalGateway
    {
        public async Task<Biolog.Features.Biometrics.Domain.BiometricTemplate?> GetLatestApprovedReferenceAsync(
            string tenantId, string personId, CancellationToken cancellationToken = default)
        {
            var reference = await faceEnrolmentService.GetLatestReferenceAsync(tenantId, personId, cancellationToken);
            return reference?.Template;
        }
    }
}
