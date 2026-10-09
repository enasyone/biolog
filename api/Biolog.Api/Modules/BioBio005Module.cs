using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FaceEnrolment.Quality;
using Biolog.Features.FaceVerification.Domain;
using Biolog.Features.FaceVerification.Service;

namespace Biolog.Api.Modules;

/// <summary>
/// Registers BIO-BIO-005's face 1:1 verification into the host (BIO-FND-008-AC05 extension
/// seam), the same thin-adapter shape every prior BIO-BIO module established: this module lives
/// here, in <c>Biolog.Api.Modules</c>, never inside <c>api/features/bio-bio-005/**</c>, so the
/// dependency between <c>Biolog.Api</c> and the feature project stays one-way.
///
/// <para>
/// This module resolves BIO-BIO-001's own already-registered <see cref="IFaceProvider"/>/
/// <see cref="IBiometricMatcher"/> singletons and BIO-BIO-003's own already-registered
/// <see cref="IFaceQualityChecker"/> singleton rather than registering second copies - there is
/// exactly one of each in the host, reused by every story that needs them (see the feature
/// project's csproj remarks for why BIO-BIO-003's quality contract is a direct
/// <c>ProjectReference</c> while its enrolment service is not).
/// </para>
///
/// <para>
/// <see cref="FaceReferenceGatewayAdapter"/> wraps BOTH BIO-BIO-003's own already-registered
/// <c>FaceEnrolmentService.GetLatestReferenceAsync</c> AND BIO-PHO-001's own already-registered
/// <c>PhotoMasterService.GetLatestAsync</c> - applying BIO-IDN-002-QA-01's lesson the same way
/// <see cref="BioBio003Module"/>'s own <c>PhotoMasterExistenceGatewayAdapter</c> and
/// <see cref="BioBio004Module"/>'s own <c>IdentityStatusGatewayAdapter</c> already do.
/// <c>FaceEnrolmentService</c>/<c>PhotoMasterService</c> are both Scoped, so
/// <see cref="FaceVerificationService"/> is registered Scoped too - a Singleton cannot safely
/// capture a Scoped dependency (captive-dependency avoidance, the same reasoning
/// <see cref="BioBio003Module"/>/<see cref="BioBio004Module"/> document for their own services).
/// </para>
///
/// <para>
/// <b>BIO-BIO-005-QA-01 (Chioma acceptance, High).</b> The gateway's second method,
/// <see cref="Domain.IFaceReferenceGateway.GetCurrentPhotoMasterVersionAsync"/>, was added
/// specifically to close the photo-lifecycle gap an acceptance probe found: resolving only the
/// latest face REFERENCE is not enough to satisfy AC04's "reference/photo lifecycle changes are
/// handled deterministically" wording, since a photo can be superseded (BIO-PHO-001) without
/// forcing a face re-capture. This adapter now also depends on <c>PhotoMasterService</c>, making
/// <c>DependsOn</c> below include <c>"Photography"</c> as a genuine runtime dependency, not just a
/// declared story dependency.
/// </para>
///
/// <para>
/// <see cref="Domain.IFaceVerificationEvidenceStore"/>, its <c>InMemory</c> implementation and
/// <see cref="FaceVerificationService"/> are all reachable here only because of the feature
/// project's explicit <c>InternalsVisibleTo</c> grant (BIO-IDN-001-QA-01's governance shape).
/// </para>
///
/// <para>
/// No HTTP endpoints are mapped by this module, the same deferred-integration judgment call
/// every prior BIO-BIO module made: AC01-AC04 are about the verification/evidence/decision
/// MODEL, and the service takes a caller-resolved tenant/person id rather than resolving one
/// itself, so a later story can map real endpoints onto <see cref="FaceVerificationService"/>
/// without this feature's shape changing.
/// </para>
/// </summary>
public sealed class BioBio005Module : IBiologModule
{
    public string Name => "FaceVerification";
    public IReadOnlyCollection<string> DependsOn => ["Biometrics", "FaceEnrolment", "Photography"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFaceVerificationEvidenceStore, InMemoryFaceVerificationEvidenceStore>();
        services.AddSingleton<IFaceVerificationThresholdPolicy, SyntheticFaceVerificationThresholdPolicy>();
        services.AddScoped<IFaceReferenceGateway>(sp => new FaceReferenceGatewayAdapter(
            sp.GetRequiredService<Biolog.Features.FaceEnrolment.Service.FaceEnrolmentService>(),
            sp.GetRequiredService<Biolog.Features.Photography.PhotoMasterService>()));

        services.AddScoped<FaceVerificationService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }

    private sealed class FaceReferenceGatewayAdapter(
        Biolog.Features.FaceEnrolment.Service.FaceEnrolmentService faceEnrolmentService,
        Biolog.Features.Photography.PhotoMasterService photoMasterService)
        : IFaceReferenceGateway
    {
        public async Task<FaceReferenceSnapshot?> GetLatestApprovedReferenceAsync(
            string tenantId, string personId, CancellationToken cancellationToken = default)
        {
            var reference = await faceEnrolmentService.GetLatestReferenceAsync(tenantId, personId, cancellationToken);
            if (reference is null)
            {
                return null;
            }

            return new FaceReferenceSnapshot
            {
                ReferenceId = reference.Id,
                Template = reference.Template,
                SourcePhotoMasterVersion = reference.SourcePhotoMasterVersion,
            };
        }

        public async Task<int?> GetCurrentPhotoMasterVersionAsync(
            string tenantId, string personId, CancellationToken cancellationToken = default)
        {
            var current = await photoMasterService.GetLatestAsync(personId, cancellationToken);
            return current?.Version;
        }
    }
}
