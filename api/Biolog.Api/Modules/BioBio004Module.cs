using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FingerprintVerification.Domain;
using Biolog.Features.FingerprintVerification.Service;

namespace Biolog.Api.Modules;

/// <summary>
/// Registers BIO-BIO-004's 1:1 fingerprint verification into the host (BIO-FND-008-AC05
/// extension seam), the same thin-adapter shape <see cref="BioBio001Module"/>/
/// <see cref="BioBio002Module"/>/<see cref="BioBio003Module"/> established: this module lives
/// here, in <c>Biolog.Api.Modules</c>, never inside <c>api/features/bio-bio-004/**</c>, so the
/// dependency between <c>Biolog.Api</c> and the feature project stays one-way.
///
/// <para>
/// This module resolves BIO-BIO-001's own already-registered <see cref="IBiometricTemplateStore"/>,
/// <see cref="IFingerprintProvider"/> and <see cref="IBiometricMatcher"/> singletons rather than
/// registering second copies - there is exactly one of each in the host, reused by every story
/// that needs them (see the feature project's csproj remarks for why this is a direct
/// <c>ProjectReference</c> rather than a gateway: BIO-BIO-001 is a plain, host-agnostic contracts
/// library, the opposite shape from BIO-IDN-001's tenant-scoped <c>PersonService</c> below).
/// </para>
///
/// <para>
/// <see cref="IdentityStatusGatewayAdapter"/> wraps BIO-IDN-001's own already-registered
/// <c>PersonService.GetAsync</c> directly - applying BIO-IDN-002-QA-01's lesson the same way
/// <see cref="BioBio003Module"/>'s own <c>PhotoMasterExistenceGatewayAdapter</c> and
/// <see cref="BioIdn004Module"/>'s own <c>PersonLifecycleGatewayAdapter</c> already do.
/// <c>PersonService</c> is Scoped (it reads a per-request tenant context), so
/// <see cref="FingerprintVerificationService"/> is registered Scoped too - a Singleton cannot
/// safely capture a Scoped dependency (captive-dependency avoidance, the same reasoning
/// <see cref="BioBio003Module"/> documents for its own service).
/// </para>
///
/// <para>
/// <see cref="Domain.IFingerprintVerificationRecordStore"/>, its <c>InMemory</c> implementation
/// and <see cref="FingerprintVerificationService"/> are all reachable here only because of the
/// feature project's explicit <c>InternalsVisibleTo</c> grant (BIO-IDN-001-QA-01's governance
/// shape, mirrored by BIO-BIO-002/003).
/// </para>
///
/// <para>
/// No HTTP endpoints are mapped by this module, the same deferred-integration judgment call
/// <see cref="BioBio001Module"/>/<see cref="BioBio002Module"/>/<see cref="BioBio003Module"/>
/// made: AC01-AC04 are about the verification/decision MODEL, and the service takes a
/// caller-resolved tenant/person id rather than resolving one itself, so a later story can map
/// real endpoints onto <see cref="FingerprintVerificationService"/> without this feature's shape
/// changing.
/// </para>
/// </summary>
public sealed class BioBio004Module : IBiologModule
{
    public string Name => "FingerprintVerification";
    public IReadOnlyCollection<string> DependsOn => ["Biometrics", "PersonRegistry"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFingerprintVerificationRecordStore, InMemoryFingerprintVerificationRecordStore>();
        services.AddScoped<IIdentityStatusGateway>(
            sp => new IdentityStatusGatewayAdapter(sp.GetRequiredService<Biolog.Features.PersonRegistry.PersonService>()));

        services.AddScoped<FingerprintVerificationService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }

    private sealed class IdentityStatusGatewayAdapter(Biolog.Features.PersonRegistry.PersonService personService)
        : IIdentityStatusGateway
    {
        public async Task<IdentityStatus?> GetStatusAsync(string personId, CancellationToken cancellationToken = default)
        {
            var person = await personService.GetAsync(personId, cancellationToken);
            if (person is null)
            {
                return null;
            }

            var wireStatus = Biolog.Features.PersonRegistry.Domain.PersonStatusExtensions.ToWireString(person.Status);
            return IdentityStatusExtensions.TryParse(wireStatus, out var status) ? status : null;
        }
    }
}
