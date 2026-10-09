using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Service;
using Biolog.Features.Biometrics.Synthetic;

namespace Biolog.Api.Modules;

/// <summary>
/// Registers BIO-BIO-001's provider-neutral biometric service/provider abstraction into the
/// host (BIO-FND-008-AC05 extension seam), the same thin-adapter shape
/// <see cref="DocumentModule"/> established: this module lives here, in
/// <c>Biolog.Api.Modules</c>, never inside <c>api/features/bio-bio-001/**</c>, so the
/// dependency between <c>Biolog.Api</c> and the feature project stays one-way
/// (<c>scripts/check-module-project-references.sh</c>).
///
/// <para>
/// Only the SYNTHETIC provider/store/matcher implementations are wired here (AC05: no real
/// biometric data is required for local development) - <see cref="SyntheticFingerprintProvider"/>,
/// <see cref="SyntheticFaceProvider"/>, <see cref="InMemoryBiometricTemplateStore"/> and
/// <see cref="SyntheticBiometricMatcher"/>. Every one of them is registered against its
/// interface, not its concrete type, so a later story swapping in a real vendor SDK adapter or a
/// real (e.g. MySQL-backed) store/matcher only changes this module's <c>RegisterServices</c>
/// body, never <see cref="BiometricService"/> or any caller of it.
/// </para>
///
/// <para>
/// No HTTP endpoints are mapped by this module. Like <see cref="DocumentModule"/> before it,
/// AC01-AC05 are about the provider/service CONTRACT - "provider-neutral biometric contracts so
/// vendor SDKs do not leak into core identity logic" - not an HTTP surface, and per ADR-0002
/// every tenant-plane write must be constructed from a resolved tenant context that a caller
/// (e.g. a future enrolment endpoint) supplies; this service takes tenant/person ids as
/// parameters rather than resolving them itself, so a later story can map real endpoints onto
/// <see cref="BiometricService"/> without this feature's service/provider shape changing.
/// </para>
/// </summary>
public sealed class BioBio001Module : IBiologModule
{
    public string Name => "Biometrics";
    public IReadOnlyCollection<string> DependsOn => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFingerprintProvider, SyntheticFingerprintProvider>();
        services.AddSingleton<IFaceProvider, SyntheticFaceProvider>();
        services.AddSingleton<IBiometricTemplateStore, InMemoryBiometricTemplateStore>();
        services.AddSingleton<IBiometricMatcher, SyntheticBiometricMatcher>();
        services.AddSingleton<BiometricService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
