using System.Threading;
using Biolog.Features.CardProduction.Audit;
using Biolog.Features.CardProduction.Domain;
using Biolog.Features.CardProduction.Identifiers;
using Xunit;

namespace Biolog.Features.CardProduction.Tests;

/// Independent Seyi probe: concurrent callers must not consume one bearer package twice.
public sealed class SeyiRound3ConcurrencyProbe
{
    [Fact]
    public async Task Concurrent_offline_completion_has_one_winner()
    {
        const string tenant = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
        const string person = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
        const string cardClass = "staff";
        var jobs = new InMemoryProductionJobRepository();
        var packages = new InMemoryProductionPackageStore();
        var gateway = new FakeCardIssueGateway();
        gateway.Seed(person, cardClass, 1);
        var service = new ProductionService(new FakeCurrentTenantProvider(tenant), jobs, packages,
            new InMemoryProductionSigningKeyStore(), gateway, new InMemoryProductionAuditSink(),
            new ProductionJobLockRegistry(), new InMemoryIdempotencyStore(), new FakeCardActivationBarrier());
        var actor = new AuditActor(ActorType.User, "seyi-probe");
        var (package, token) = await service.IssuePackageAsync(person, cardClass, 1, "p", "w", null, null,
            actor, CanonicalId.NewEventId(), CancellationToken.None);
        using var gate = new Barrier(2);
        async Task<bool> Attempt(string key)
        {
            gate.SignalAndWait();
            try
            {
                await service.CompleteOfflineJobAsync(token, true, null, true, null, true, null,
                    key, actor, CanonicalId.NewEventId(), CancellationToken.None);
                return true;
            }
            catch (ProductionError) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(() => Attempt("probe-a")), Task.Run(() => Attempt("probe-b")));
        Assert.Equal(1, results.Count(x => x));
        Assert.Single(jobs.List(tenant, person, cardClass, 1));
        Assert.True(packages.Get(tenant, package.PackageId)!.Consumed);
    }
}
