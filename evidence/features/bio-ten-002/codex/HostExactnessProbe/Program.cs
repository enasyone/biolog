using Biolog.Features.TenantDomainRegistry;
using Biolog.Features.TenantDomainRegistry.Api;
using Biolog.Features.TenantDomainRegistry.Audit;
using Biolog.Features.TenantDomainRegistry.Domain;
using Biolog.Features.TenantDomainRegistry.Resolution;

const string tenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
var service = new TenantDomainService(new InMemoryTenantDomainRegistry(), new InMemoryTenantDomainAuditSink());
var actor = new AuditActor(ActorType.User, "synthetic-reviewer");
var registered = await service.RegisterAsync(
    new RegisterDomainRequest { Hostname = "portal.example.edu.ng", TenantId = tenantId },
    actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a79");
await service.UpdateStatusAsync(
    registered.Hostname,
    new UpdateDomainStatusRequest { Version = registered.Version, Status = "VERIFIED" },
    actor, "evt_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a80");

var hostileHosts = new[]
{
    "portal.example.edu.ng:evil",
    "portal.example.edu.ng:",
    "portal.example.edu.ng:443.attacker.invalid",
};

var failures = 0;
foreach (var hostile in hostileHosts)
{
    var normalized = HostNormalizer.Normalize(hostile);
    try
    {
        var resolved = await service.ResolveAsync(hostile);
        Console.WriteLine($"FAIL accepted '{hostile}' as '{normalized}' for {resolved.TenantId}");
        failures++;
    }
    catch (TenantDomainError error) when (error.Code == "host_not_recognised")
    {
        Console.WriteLine($"PASS rejected '{hostile}'");
    }
}

return failures == 0 ? 0 : 1;
