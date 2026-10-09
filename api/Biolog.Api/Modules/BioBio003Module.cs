using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FaceEnrolment.Domain;
using Biolog.Features.FaceEnrolment.Liveness;
using Biolog.Features.FaceEnrolment.Quality;
using Biolog.Features.FaceEnrolment.Service;

namespace Biolog.Api.Modules;

/// <summary>
/// Registers BIO-BIO-003's quality-gated face enrolment capture into the host
/// (BIO-FND-008-AC05 extension seam), the same thin-adapter shape <see cref="BioBio001Module"/>/
/// <see cref="BioBio002Module"/> established: this module lives here, in
/// <c>Biolog.Api.Modules</c>, never inside <c>api/features/bio-bio-003/**</c>, so the dependency
/// between <c>Biolog.Api</c> and the feature project stays one-way.
///
/// <para>
/// <see cref="PhotoMasterExistenceGatewayAdapter"/> wraps BIO-PHO-001's own already-registered
/// <c>PhotoMasterService.GetVersionAsync</c> directly - applying BIO-IDN-002-QA-01's lesson the
/// same way <see cref="BioPho001Module"/> itself already does for its own person-existence
/// gateway. <c>PhotoMasterService</c> is Scoped (it reads a per-request tenant context), so
/// <see cref="FaceEnrolmentService"/> is registered Scoped too - a Singleton cannot safely
/// capture a Scoped dependency (captive-dependency avoidance, the same reasoning
/// <c>PersonLockRegistry</c>'s header documents).
/// </para>
///
/// <para>
/// <see cref="Domain.IFaceBiometricReferenceRepository"/>, its <c>InMemory</c> implementation,
/// the quality checker and the liveness detector are all internal-visibility-independent of this
/// module (only the repository/service are actually <c>internal</c> types, reachable via the
/// feature project's <c>InternalsVisibleTo</c> grant). This module resolves the
/// <c>IFaceProvider</c> singleton BIO-BIO-001's own <see cref="BioBio001Module"/> already
/// registers rather than registering a second one - there is exactly one synthetic face
/// provider in the host, reused by every story that needs it.
/// </para>
///
/// <para>
/// No HTTP endpoints are mapped by this module, the same deferred-integration judgment call
/// <see cref="BioBio001Module"/>/<see cref="BioBio002Module"/> made: AC01-AC05 are about the
/// capture/quality-gate MODEL, and the service takes a caller-resolved tenant/person id rather
/// than resolving one itself, so a later story can map real endpoints onto
/// <see cref="FaceEnrolmentService"/> without this feature's shape changing.
/// </para>
/// </summary>
public sealed class BioBio003Module : IBiologModule
{
    public string Name => "FaceEnrolment";
    public IReadOnlyCollection<string> DependsOn => ["Biometrics", "Photography"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFaceBiometricReferenceRepository, InMemoryFaceBiometricReferenceRepository>();
        services.AddSingleton<IFaceQualityChecker, SyntheticFaceQualityChecker>();
        services.AddSingleton<ILivenessDetector, SyntheticLivenessDetector>();
        services.AddScoped<IPhotoMasterExistenceGateway>(
            sp => new PhotoMasterExistenceGatewayAdapter(sp.GetRequiredService<Biolog.Features.Photography.PhotoMasterService>()));

        services.AddScoped<FaceEnrolmentService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }

    private sealed class PhotoMasterExistenceGatewayAdapter(Biolog.Features.Photography.PhotoMasterService photoMasterService)
        : IPhotoMasterExistenceGateway
    {
        public async Task<bool> ExistsAsync(string personId, int photoMasterVersion, CancellationToken cancellationToken = default) =>
            await photoMasterService.GetVersionAsync(personId, photoMasterVersion, cancellationToken) is not null;
    }
}
