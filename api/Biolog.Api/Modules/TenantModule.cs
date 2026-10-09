using Biolog.Api.Audit;
using Biolog.Api.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Registers the tenant middleware hook (BIO-FND-008-AC04) and BIO-TEN-002's host-channel
/// resolution. See <see cref="TenantContextMiddleware"/> for what it resolves and does not.
///
/// <see cref="TenantContextAccessor"/> is registered as itself first, then
/// <see cref="ITenantContextAccessor"/> resolves to that same scoped instance (BIO-TEN-002-
/// QA-03, Seyi round 3) - <see cref="TenantContextMiddleware"/> takes the concrete type so it
/// can reach the internal <c>SetResolved</c>, while every other consumer takes the read-only
/// interface, and both see the one instance the middleware populated for this request.
///
/// <see cref="DemoSessionContextAuditEndpoints"/>'s route (OPS002-F02 fix, 2026-10-07) is
/// registered here too - the same demo-session-bridge mechanism this module already owns via
/// <see cref="DemoSessionTenantResolver"/>, not a separate feature project (see that endpoints
/// class's own header for why). Its audit sink is registered by the CONCRETE
/// <see cref="InMemoryAuditSink"/> type, deliberately NOT through the shared
/// <see cref="IAuditSink"/> interface slot <see cref="DeviceRegistryModule"/> already owns -
/// see <see cref="DemoSessionContextAuditEndpoints"/>'s own header for why a second
/// <c>AddSingleton&lt;IAuditSink, ...&gt;()</c> here would silently collide with that one.
/// </summary>
public sealed class TenantModule : IBiologModule
{
    public string Name => "Tenant";
    public IReadOnlyCollection<string> DependsOn => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(sp => sp.GetRequiredService<TenantContextAccessor>());
        services.AddSingleton(_ => DemoSessionTenantResolver.LoadFromConfiguration(configuration));
        services.AddSingleton<InMemoryAuditSink>();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseMiddleware<TenantContextMiddleware>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        DemoSessionContextAuditEndpoints.Map(endpoints);
    }
}
