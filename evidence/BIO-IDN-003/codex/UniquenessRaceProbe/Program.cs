using Biolog.Features.InstitutionalIdentifiers;
using Biolog.Features.InstitutionalIdentifiers.Api;
using Biolog.Features.InstitutionalIdentifiers.Audit;
using Biolog.Features.InstitutionalIdentifiers.Domain;
using Biolog.Features.InstitutionalIdentifiers.Identifiers;
using Biolog.Features.InstitutionalIdentifiers.Tenancy;

const string tenant = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
const string personA = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a91";
const string personB = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a92";

var inner = new InMemoryInstitutionalIdentifierRepository();
var repository = new CoordinatedRepository(inner);
var audit = new InMemoryInstitutionalIdentifierAuditSink();
var rules = new InMemoryIdentifierUniquenessRuleRepository(); // absent rule = enforced
var locks = new InstitutionalIdentifierLockRegistry();
var gateway = new ExistingPersonGateway();
var service = new InstitutionalIdentifierService(new FixedTenant(tenant), repository, rules, audit, locks, gateway);
var actor = new AuditActor(ActorType.User, "synthetic_admin");
var request = new AssignIdentifierRequest { Type = "MATRICULATION_NUMBER", Value = "MAT/RACE/001" };

var results = await Task.WhenAll(
    service.AssignAsync(personA, request, actor, CanonicalId.NewEventId()),
    service.AssignAsync(personB, request, actor, CanonicalId.NewEventId()));

if (results.Length != 2 || results.Any(x => x.Value != "MAT/RACE/001"))
    throw new Exception("expected both conflicting assignments to commit");
if (audit.Events.Count != 2)
    throw new Exception("expected two false-success ALLOWED audits");

Console.WriteLine("REPRODUCED: enforced tenant-wide uniqueness admitted two concurrent identical values");

file sealed class FixedTenant(string tenantId) : ICurrentTenantProvider
{
    public string? TenantId => tenantId;
}

file sealed class ExistingPersonGateway : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

file sealed class CoordinatedRepository(IInstitutionalIdentifierRepository inner) : IInstitutionalIdentifierRepository
{
    private readonly TaskCompletionSource _bothChecked = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _checks;

    public Task<InstitutionalIdentifier> CreateAsync(InstitutionalIdentifier identifier, CancellationToken cancellationToken = default) =>
        inner.CreateAsync(identifier, cancellationToken);

    public Task<InstitutionalIdentifier?> GetActiveAsync(string tenantId, string personId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default) =>
        inner.GetActiveAsync(tenantId, personId, type, customLabel, cancellationToken);

    public Task<InstitutionalIdentifier?> GetAsync(string tenantId, string personId, IdentifierType type, string? customLabel, int sequenceNumber, CancellationToken cancellationToken = default) =>
        inner.GetAsync(tenantId, personId, type, customLabel, sequenceNumber, cancellationToken);

    public Task<IReadOnlyList<InstitutionalIdentifier>> ListHistoryAsync(string tenantId, string personId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default) =>
        inner.ListHistoryAsync(tenantId, personId, type, customLabel, cancellationToken);

    public Task<IReadOnlyList<InstitutionalIdentifier>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default) =>
        inner.ListByPersonAsync(tenantId, personId, cancellationToken);

    public async Task<bool> ValueExistsAsync(string tenantId, IdentifierType type, string? customLabel, string value, CancellationToken cancellationToken = default)
    {
        var captured = await inner.ValueExistsAsync(tenantId, type, customLabel, value, cancellationToken);
        if (Interlocked.Increment(ref _checks) == 2)
            _bothChecked.TrySetResult();
        await _bothChecked.Task.WaitAsync(cancellationToken);
        return captured;
    }

    public Task<InstitutionalIdentifier> ReplaceAsync(InstitutionalIdentifier expected, InstitutionalIdentifier updated, CancellationToken cancellationToken = default) =>
        inner.ReplaceAsync(expected, updated, cancellationToken);
}
