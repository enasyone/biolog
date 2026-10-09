using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.FingerprintSearch.Domain;
using Biolog.Features.FingerprintSearch.Service;

namespace Biolog.Api.Modules;

/// <summary>
/// Registers BIO-BIO-006's 1:N fingerprint duplicate/identification search into the host
/// (BIO-FND-008-AC05 extension seam), the same thin-adapter shape every prior BIO-BIO module
/// established: this module lives here, in <c>Biolog.Api.Modules</c>, never inside
/// <c>api/features/bio-bio-006/**</c>, so the dependency between <c>Biolog.Api</c> and the
/// feature project stays one-way.
///
/// <para>
/// This module resolves BIO-BIO-001's own already-registered <see cref="IFingerprintProvider"/>/
/// <see cref="IBiometricMatcher"/> singletons rather than registering second copies - there is
/// exactly one of each in the host, reused by every story that needs them.
/// </para>
///
/// <para>
/// <see cref="BiometricSearchPermissionGatewayAdapter"/> wraps BIO-IDN-006's own already-
/// registered <c>PermissionService.ActorHasPermissionAsync</c> directly - the same "narrow
/// gateway + host-level adapter" shape every prior BIO-BIO module uses for its own tenant-scoped
/// upstream dependency. <c>PermissionService</c> is Scoped (it reads a per-request tenant
/// context), so <see cref="FingerprintSearchService"/> is registered Scoped too - a Singleton
/// cannot safely capture a Scoped dependency (captive-dependency avoidance, the same reasoning
/// every prior BIO-BIO module documents for its own service).
/// </para>
///
/// <para>
/// <see cref="Domain.IFingerprintGalleryStore"/>, <see cref="Domain.IFingerprintDuplicateReviewRepository"/>,
/// their <c>InMemory</c> implementations and <see cref="FingerprintSearchService"/> are all
/// reachable here only because of the feature project's explicit <c>InternalsVisibleTo</c> grant
/// (BIO-IDN-001-QA-01's governance shape).
/// </para>
///
/// <para>
/// No HTTP endpoints are mapped by this module, the same deferred-integration judgment call
/// every prior BIO-BIO module made: AC01-AC05 are about the search/review MODEL, and the service
/// takes a caller-resolved tenant/person/actor id rather than resolving one itself, so a later
/// story can map real endpoints onto <see cref="FingerprintSearchService"/> without this
/// feature's shape changing.
/// </para>
/// </summary>
public sealed class BioBio006Module : IBiologModule
{
    public string Name => "FingerprintSearch";
    public IReadOnlyCollection<string> DependsOn => ["Biometrics", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IFingerprintGalleryStore, InMemoryFingerprintGalleryStore>();
        services.AddSingleton<IFingerprintDuplicateReviewRepository, InMemoryFingerprintDuplicateReviewRepository>();
        services.AddSingleton<IFingerprintDuplicateThresholdPolicy, SyntheticFingerprintDuplicateThresholdPolicy>();
        services.AddSingleton<FingerprintDuplicateReviewLockRegistry>();
        services.AddScoped<IBiometricSearchPermissionGateway>(
            sp => new BiometricSearchPermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));

        services.AddScoped<FingerprintSearchService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }

    private sealed class BiometricSearchPermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService)
        : IBiometricSearchPermissionGateway
    {
        public Task<bool> HasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }
}
