using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Api.Cards;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// BIO-OPS-003-AC04 round 2 (SY-OPS003-02, Kemi's ruling): end-to-end proof, through the REAL
/// packaged <c>Biolog.Api</c> host, that BIO-CRD-006 and BIO-CRD-007 genuinely serialise against
/// the SAME shared <see cref="CardActivationBarrierLockRegistry"/> singleton - not merely that
/// each has its own, independent lock. Seyi's own request: hold the shared lock for a given key
/// from one side via a controllable gate (here, the test itself acquiring it directly from the
/// real, resolved singleton - the same object both modules' adapters wrap), and assert the
/// OTHER side's real call genuinely blocks until it is released. A real dual-service race (two
/// concurrent HTTP calls, "may the fastest caller win") would be nondeterministic and prove
/// nothing about WHICH lock either side actually acquired; holding the lock directly and
/// asserting the real call stays pending is a deterministic proof that the real code path
/// genuinely reaches this exact shared object, not an independent one of its own.
///
/// Two directions, since either feature's own `ActivateReplacementAsync`/`ProductionService`
/// entry points could independently have been wired to the WRONG (or no) lock - proving one
/// direction says nothing about the other.
/// </summary>
public sealed class CardActivationBarrierHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5d77";

    public CardActivationBarrierHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    private static HttpRequestMessage DomainAdminRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Actor-Id", "admin_alice");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string host, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", "operator_ada");
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<string> RegisterAndVerifyDomainAsync(HttpClient client, string hostname, string tenantId, CancellationToken ct)
    {
        var registerResponse = await client.SendAsync(
            DomainAdminRequest(HttpMethod.Post, "/api/v1/control-plane/tenant-domains", new { hostname, tenant_id = tenantId }), ct);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var registered = await registerResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = registered.GetProperty("version").GetInt32();

        var verifyResponse = await client.SendAsync(
            DomainAdminRequest(HttpMethod.Put, $"/api/v1/control-plane/tenant-domains/{hostname}/status", new { version, status = "VERIFIED" }), ct);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        return hostname;
    }

    /// <summary>Registers a person and a real, ACTIVE BIO-CRD-005 card issue (sequence 1), then replaces it to get an Issued sequence 2 - the key both test directions contend on.</summary>
    private async Task<(string PersonId, int ReplacementVersion)> IssueActivateAndReplaceAsync(HttpClient client, string host, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons", host, new { first_name = "Ngozi", last_name = "Okafor" }), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var personId = (await response.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff", host,
            new { template_ref = "t1", photo_ref = "p1", printer_ref = "printer-e2e" }), ct);
        var activateResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/1/activate", host, new { expected_version = 1 }, idempotencyKey: $"barrier-e2e-activate-{personId}"), ct);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        var replaceResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/replace", host, new { template_ref = "t2", printer_ref = "p2" }), ct);
        Assert.Equal(HttpStatusCode.Created, replaceResponse.StatusCode);
        var replacement = await replaceResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(2, replacement.GetProperty("sequence_number").GetInt32());

        return (personId, replacement.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Holding_the_shared_barrier_blocks_a_real_CRD006_activate_replacement_call_until_released()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "barrier-e2e-crd006.example.edu.ng", TenantA, ct);
        var (personId, replacementVersion) = await IssueActivateAndReplaceAsync(client, hostname, ct);

        // A real production job for the replacement (sequence 2), Passed - so once the barrier
        // is released, the real activation genuinely succeeds rather than being refused for an
        // unrelated reason (production_not_passed), which would prove nothing about the lock.
        var createResponse = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/2/production/jobs", hostname,
            new { printer_ref = "printer-e2e", workstation_ref = "workstation-e2e" }), ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
        var afterPrint = await RecordStepAsync(client, hostname, personId, "print", created.GetProperty("version").GetInt32(), ct);
        var afterEncode = await RecordStepAsync(client, hostname, personId, "encode", afterPrint, ct);
        await RecordStepAsync(client, hostname, personId, "readback-qc", afterEncode, ct);

        var registry = _factory.Services.GetRequiredService<CardActivationBarrierLockRegistry>();
        var held = await registry.AcquireAsync(TenantA, personId, "staff", 2, CancellationToken.None);

        var activateTask = Task.Run(() => client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/2/activate-replacement", hostname,
            new { expected_version = replacementVersion }, idempotencyKey: $"barrier-e2e-activate-replacement-{personId}"), ct));

        var completedEarly = await Task.WhenAny(activateTask, Task.Delay(TimeSpan.FromMilliseconds(400), ct));
        Assert.NotSame(activateTask, completedEarly); // still genuinely blocked - the real ActivateReplacementAsync is waiting on THIS exact held lock

        held.Dispose();

        var activateResponse = await activateTask.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
    }

    [Fact]
    public async Task Holding_the_shared_barrier_blocks_a_real_CRD007_create_job_call_until_released()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "barrier-e2e-crd007.example.edu.ng", TenantA, ct);
        var (personId, _) = await IssueActivateAndReplaceAsync(client, hostname, ct);

        var registry = _factory.Services.GetRequiredService<CardActivationBarrierLockRegistry>();
        var held = await registry.AcquireAsync(TenantA, personId, "staff", 2, CancellationToken.None);

        var createTask = Task.Run(() => client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/2/production/jobs", hostname,
            new { printer_ref = "printer-e2e", workstation_ref = "workstation-e2e" }), ct));

        var completedEarly = await Task.WhenAny(createTask, Task.Delay(TimeSpan.FromMilliseconds(400), ct));
        Assert.NotSame(createTask, completedEarly); // still genuinely blocked - the real CreateJobAsync is waiting on THIS exact held lock

        held.Dispose();

        var createResponse = await createTask.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
    }

    private async Task<int> RecordStepAsync(HttpClient client, string host, string personId, string step, int expectedVersion, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/staff/2/production/jobs/1/steps/{step}", host,
            new { passed = true, expected_version = expectedVersion }), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("version").GetInt32();
    }
}
