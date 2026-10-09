using Biolog.Features.CardIssuance;
using Biolog.Features.CardIssuance.Audit;
using Biolog.Features.CardIssuance.Domain;
using Biolog.Features.CardIssuance.Tenancy;

const string tenant = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
const string person = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
var repository = new InMemoryCardIssueRepository();
var service = new CardIssueService(
    new TenantProvider(tenant), repository, new ExistingPerson(), new FailedAuditSink(),
    new CardIssueLockRegistry(), new InMemoryIdempotencyStore());

try
{
    await service.IssueAsync(person, "staff", new IssueCardRequest { TemplateRef = "template", PrinterRef = "printer" },
        new AuditActor(ActorType.User, "operator"), Guid.NewGuid().ToString(), CancellationToken.None);
}
catch (Exception ex)
{
    Console.WriteLine($"service-result={ex.GetType().Name}");
}

var rows = repository.List(tenant, person, "staff");
Console.WriteLine($"persisted-card-count-after-confirmed-audit-failure={rows.Count}");
if (rows.Count != 0)
{
    Console.Error.WriteLine("FAIL: confirmed audit failure left an unaudited CardIssue committed");
    return 1;
}

return 0;

sealed class TenantProvider(string tenantId) : ICurrentTenantProvider { public string? TenantId { get; } = tenantId; }
sealed class ExistingPerson : IPersonExistenceGateway { public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) => Task.FromResult(true); }
sealed class FailedAuditSink : ICardIssueAuditSink
{
    public Task WriteAsync(CardIssueAuditEvent auditEvent, CancellationToken cancellationToken = default) => throw new InvalidOperationException("audit offline");
    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
