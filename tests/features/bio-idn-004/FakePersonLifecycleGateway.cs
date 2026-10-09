using Biolog.Features.PersonLifecycle.Domain;

namespace Biolog.Features.PersonLifecycle.Tests;

/// <summary>
/// Test double for <see cref="IPersonLifecycleGateway"/>. In production this is backed by
/// BIO-IDN-001's own real, tenant-scoped <c>PersonService.GetAsync</c>/<c>UpdateStatusAsync</c>
/// - see <c>tests/dotnet/Biolog.IntegrationTests/PersonLifecycleHostChannelTests.cs</c> for the
/// real shipped-host proof. Mirrors the shape of a genuine per-person store with CAS, so tests
/// can exercise <c>person_not_found</c>/<c>concurrency_conflict</c>/<c>invalid_status_transition</c>
/// translation without a real BIO-IDN-001 dependency.
/// </summary>
internal sealed class FakePersonLifecycleGateway
{
    private readonly Dictionary<string, PersonSnapshot> _people = new();

    public void Seed(string personId, string tenantId, PersonStatus status, int version = 1) =>
        _people[personId] = new PersonSnapshot(personId, tenantId, status, version);

    public PersonSnapshot? this[string personId] => _people.TryGetValue(personId, out var snapshot) ? snapshot : null;

    private sealed class Gateway(FakePersonLifecycleGateway fake, string tenantId) : IPersonLifecycleGateway
    {
        public Task<PersonSnapshot?> GetSnapshotAsync(string personId, CancellationToken cancellationToken = default)
        {
            var snapshot = fake[personId];
            return Task.FromResult(snapshot is not null && snapshot.TenantId == tenantId ? snapshot : null);
        }

        public Task<LifecycleChangeResult> ChangeStatusAsync(
            string personId, PersonStatus targetStatus, int expectedVersion, string actorId, string actorTypeWire, string correlationId,
            CancellationToken cancellationToken = default)
        {
            var current = fake[personId];
            if (current is null || current.TenantId != tenantId)
            {
                return Task.FromResult(LifecycleChangeResult.Failed("person_not_found"));
            }

            if (current.Version != expectedVersion)
            {
                return Task.FromResult(LifecycleChangeResult.Failed("concurrency_conflict"));
            }

            if (!LifecycleTransitionPolicy.IsAllowed(current.Status, targetStatus))
            {
                return Task.FromResult(LifecycleChangeResult.Failed("invalid_status_transition"));
            }

            var updated = new PersonSnapshot(current.PersonId, current.TenantId, targetStatus, current.Version + 1);
            fake._people[personId] = updated;
            return Task.FromResult(LifecycleChangeResult.Ok(updated));
        }
    }

    public IPersonLifecycleGateway ForTenant(string tenantId) => new Gateway(this, tenantId);
}
