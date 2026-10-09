using Biolog.Features.InstitutionalIdentifiers;
using Biolog.Features.InstitutionalIdentifiers.Api;
using Biolog.Features.InstitutionalIdentifiers.Audit;
using Biolog.Features.InstitutionalIdentifiers.Domain;
using Biolog.Features.InstitutionalIdentifiers.Identifiers;
using Biolog.Features.InstitutionalIdentifiers.Tenancy;

const string tenant = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
const string personA = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a91";
const string personB = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a92";

var identifiers = new InMemoryInstitutionalIdentifierRepository();
var ruleStore = new InMemoryIdentifierUniquenessRuleRepository();
var audit = new InMemoryInstitutionalIdentifierAuditSink();
var scopeLocks = new IdentifierUniquenessRuleLockRegistry();
var tenantProvider = new FixedTenant(tenant);
var actor = new AuditActor(ActorType.User, "synthetic_admin");
var identifierService = new InstitutionalIdentifierService(
    tenantProvider, identifiers, ruleStore, audit, new InstitutionalIdentifierLockRegistry(), scopeLocks, new ExistingPersonGateway());
var ruleService = new IdentifierUniquenessRuleService(tenantProvider, ruleStore, identifiers, audit, scopeLocks);

await ruleService.SetRuleAsync(IdentifierType.MatriculationNumber, null, false, actor, CanonicalId.NewEventId());
var request = new AssignIdentifierRequest { Type = "MATRICULATION_NUMBER", Value = "MAT/DUPLICATE/001" };
await identifierService.AssignAsync(personA, request, actor, CanonicalId.NewEventId());
await identifierService.AssignAsync(personB, request, actor, CanonicalId.NewEventId());

var eventsBeforeRejectedEnable = audit.Events.Count;
try
{
    await ruleService.SetRuleAsync(IdentifierType.MatriculationNumber, null, true, actor, CanonicalId.NewEventId());
    throw new Exception("uniqueness was enabled over duplicate values");
}
catch (InstitutionalIdentifierError error) when (error.Code == "uniqueness_already_violated" && error.Status == 409)
{
}

var unchanged = await ruleService.GetRuleAsync(IdentifierType.MatriculationNumber, null);
if (unchanged is not { Enforced: false })
    throw new Exception("rejected enablement changed the stored rule");
if (audit.Events.Count != eventsBeforeRejectedEnable)
    throw new Exception("rejected enablement left a false-success audit");

Console.WriteLine("PASS: uniqueness enablement rejected over duplicate values without rule or audit mutation");

file sealed class FixedTenant(string tenantId) : ICurrentTenantProvider
{
    public string? TenantId => tenantId;
}

file sealed class ExistingPersonGateway : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
