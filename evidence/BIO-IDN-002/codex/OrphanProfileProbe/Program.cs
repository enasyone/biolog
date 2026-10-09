using Biolog.Features.PersonProfileRegistry;
using Biolog.Features.PersonProfileRegistry.Api;
using Biolog.Features.PersonProfileRegistry.Audit;
using Biolog.Features.PersonProfileRegistry.Domain;
using Biolog.Features.PersonProfileRegistry.Identifiers;
using Biolog.Features.PersonProfileRegistry.Tenancy;

const string tenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
const string foreignOrMissingPerson = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99";

var audit = new InMemoryPersonProfileAuditSink();
var service = new PersonProfileService(
    new FixedTenant(tenantB),
    new InMemoryPersonProfileRepository(),
    audit,
    new PersonProfileLockRegistry(),
    new MissingPersonGateway());

try
{
    await service.AssignAsync(
        foreignOrMissingPerson,
        new AssignProfileRequest { Type = "STUDENT" },
        new AuditActor(ActorType.User, "synthetic_admin"),
        CanonicalId.NewEventId());
    throw new Exception("orphan profile assignment unexpectedly succeeded");
}
catch (PersonProfileError error) when (error.Code == "person_not_found" && error.Status == 404)
{
}

if (audit.Events.Count != 0)
    throw new Exception("rejected assignment left a false-success audit event");

Console.WriteLine("PASS: missing/foreign PERSON rejected before profile storage or audit");

file sealed class FixedTenant(string tenantId) : ICurrentTenantProvider
{
    public string? TenantId => tenantId;
}

file sealed class MissingPersonGateway : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
