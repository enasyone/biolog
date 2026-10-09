using Biolog.Features.FingerprintEnrolment.Audit;
using Biolog.Features.FingerprintEnrolment.Domain;
using Biolog.Features.FingerprintEnrolment.Quality;
using Biolog.Features.FingerprintEnrolment.Service;

namespace Biolog.Api.Modules;

/// <summary>
/// Registers BIO-BIO-002's guided tenprint/single-finger enrolment session into the host
/// (BIO-FND-008-AC05 extension seam), the same thin-adapter shape <see cref="BioBio001Module"/>
/// established: this module lives here, in <c>Biolog.Api.Modules</c>, never inside
/// <c>api/features/bio-bio-002/**</c>, so the dependency between <c>Biolog.Api</c> and the
/// feature project stays one-way (<c>scripts/check-module-project-references.sh</c>).
///
/// <para>
/// <see cref="FingerprintEnrolmentService"/>, <see cref="IFingerprintEnrolmentRepository"/> and
/// its <see cref="InMemoryFingerprintEnrolmentRepository"/> implementation are all
/// <c>internal</c> to the feature project (BIO-IDN-001-QA-01's governance shape) - reachable here
/// only because of the explicit <c>InternalsVisibleTo</c> grant in that project's <c>.csproj</c>.
/// This module resolves the fingerprint provider BIO-BIO-001's own <see cref="BioBio001Module"/>
/// already registered (<c>IFingerprintProvider</c>) rather than registering a second one - there
/// is exactly one synthetic fingerprint provider in the host, reused by both stories.
/// </para>
///
/// <para>
/// No HTTP endpoints are mapped by this module, the same deferred-integration judgment call
/// <see cref="BioBio001Module"/> made: AC01-AC05 are about the guided-enrolment SESSION MODEL,
/// and the service takes a caller-resolved tenant/person id rather than resolving one itself, so
/// a later story can map real endpoints onto <see cref="FingerprintEnrolmentService"/> without
/// this feature's shape changing.
/// </para>
///
/// <para>
/// <b>BIO-BIO-009-AC03.</b> <see cref="FingerprintEnrolmentService"/> now requires an
/// <c>IRawImageRetentionAuthority</c> (this feature's own seam, BIO-BIO-002 cannot reference the
/// later BIO-BIO-009 story directly). <see cref="BioBio009Module"/> registers the concrete
/// adapter wrapping its own real tenant policy - registration ORDER between the two modules does
/// not matter here: both add to the same <see cref="IServiceCollection"/> before the service
/// provider is ever built, and <see cref="FingerprintEnrolmentService"/> is only ever resolved
/// afterward (see that adapter's own remarks).
/// </para>
/// </summary>
public sealed class BioBio002Module : IBiologModule
{
    public string Name => "FingerprintEnrolment";
    public IReadOnlyCollection<string> DependsOn => ["Biometrics"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFingerprintEnrolmentRepository, InMemoryFingerprintEnrolmentRepository>();
        services.AddSingleton<IFingerQualityScorer, SyntheticFingerQualityScorer>();
        services.AddSingleton<IFingerprintEnrolmentAuditSink, InMemoryFingerprintEnrolmentAuditSink>();
        services.AddSingleton<FingerprintEnrolmentLockRegistry>();
        services.AddSingleton<FingerprintEnrolmentService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
