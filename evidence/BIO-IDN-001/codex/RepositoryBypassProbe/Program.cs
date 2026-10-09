using Biolog.Features.PersonRegistry.Domain;

const string tenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
const string tenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
const string personId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99";

var repository = new InMemoryPersonRepository();
var original = new Person
{
    Id = personId,
    TenantId = tenantA,
    FirstName = "Synthetic",
    LastName = "Person",
    Status = PersonStatus.PreEnrolled,
    Version = 1,
    CreatedAt = "2026-09-22T09:00:00.000Z",
    UpdatedAt = "2026-09-22T09:00:00.000Z",
};

await repository.CreateAsync(original);

// Bypass PersonService: change both the immutable tenant boundary and lifecycle state,
// without an audit dependency or a governed transition.
var forged = original with
{
    TenantId = tenantB,
    Status = PersonStatus.Archived,
    Version = 2,
    UpdatedAt = "2026-09-22T09:01:00.000Z",
};
await repository.ReplaceAsync(original, forged);

if (await repository.GetAsync(tenantA, personId) is not null)
    throw new Exception("source tenant still sees the moved record");
if (await repository.GetAsync(tenantB, personId) is not { Status: PersonStatus.Archived })
    throw new Exception("repository did not accept the forged tenant/status mutation");

Console.WriteLine("REPRODUCED: public repository moved and archived PERSON without PersonService or audit");
