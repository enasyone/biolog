using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.AccessPolicy.Api;
using Biolog.Features.AccessPolicy.Audit;
using Biolog.Features.AccessPolicy.Domain;
using Biolog.Features.AccessPolicy.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.AccessPolicy.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-ACC-001's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and
/// <see cref="AccessPolicyEndpoints.Map"/> directly, never through
/// <c>Biolog.Api.Modules.BioAcc001Module</c> - this project has no <c>ProjectReference</c> to
/// <c>Biolog.Api.csproj</c>. Tenant context and the gateways are fakes here; the real packaged
/// pipeline is covered by <c>tests/dotnet/Biolog.IntegrationTests/AccessPolicyHostChannelTests.cs</c>.
/// </summary>
public sealed class AccessPolicyEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string AuthorId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PublisherId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1e";
    private const string SubjectPersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1f";
    private const string ProvenanceSecret = "test-provenance-secret-9f8e7d-32ok";

    private static string ComputeProvenanceProof(string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

    public async ValueTask InitializeAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(new FakeCurrentTenantProvider(TenantId));
                services.AddSingleton<IAccessPolicyRepository, InMemoryAccessPolicyRepository>();
                services.AddSingleton<IAccessPolicyAuditSink, InMemoryAccessPolicyAuditSink>();
                services.AddSingleton<AccessPolicyLockRegistry>();
                services.AddSingleton<IPersonExistenceGateway>(new FakePersonExistenceGateway(new HashSet<string> { SubjectPersonId }));
                services.AddSingleton<IIdentityStatusGateway>(new FakeIdentityStatusGateway(
                    new Dictionary<string, IdentityStatus> { [SubjectPersonId] = IdentityStatus.Active }));
                services.AddSingleton<IPermissionGateway>(new FakePermissionGateway());
                services.AddScoped<AccessPolicyService>();
                services.AddScoped<PolicyEvaluationService>();
                services.AddSingleton<IActorProvenanceStore>(_ =>
                {
                    var store = new HmacActorProvenanceStore();
                    store.Configure(ProvenanceSecret);
                    return store;
                });
                services.AddSingleton<ActorProvenanceGate>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => AccessPolicyEndpoints.Map(endpoints));
            });
        });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync(TestContext.Current.CancellationToken);
        _host.Dispose();
    }

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string actorId, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(ActorResolver.ActorIdHeader, actorId);
        request.Headers.Add(ActorProvenanceGate.ProvenanceHeader, ComputeProvenanceProof(actorId));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: AccessPolicyJsonOptions.Strict);
        }

        return request;
    }

    private static object FullRule(string effect = "ALLOW", string requiredAssurance = "BASIC") => new
    {
        effect,
        subject = new { kind = "ANY_PERSON" },
        credential = new { kind = "SPECIFIC_CLASS", credential_class = "staff-card" },
        location = new { kind = "SPECIFIC_LOCATION", location_id = "main-gate" },
        schedule = new { kind = "ALWAYS" },
        identity_status = new { any_status = false, allowed_statuses = new[] { "ACTIVE" } },
        card_status = new { any_status = true },
        required_assurance_level = requiredAssurance,
    };

    private async Task<(string PolicyKey, int Version)> CreateDraftAsync(CancellationToken ct)
    {
        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, "/api/v1/access-policies", AuthorId, new { policy_key = "main-campus", rules = new[] { FullRule() } }), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return ("main-campus", body.GetProperty("concurrency_version").GetInt32());
    }

    // --- AC01/AC02: creation, versioning ---

    [Fact]
    public async Task Creating_a_draft_returns_201_with_a_single_draft_version_and_a_correlation_id_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, "/api/v1/access-policies", AuthorId, new { policy_key = "main-campus", rules = new[] { FullRule() } }), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdHeader.HeaderName));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("DRAFT", body.GetProperty("latest_version").GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("latest_version").GetProperty("version_number").GetInt32());
    }

    [Fact]
    public async Task Publishing_then_getting_shows_the_published_version_number()
    {
        var ct = TestContext.Current.CancellationToken;
        var (policyKey, version) = await CreateDraftAsync(ct);

        var publish = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/access-policies/{policyKey}/publish", PublisherId, new { version }), ct);
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var get = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/access-policies/{policyKey}", PublisherId), ct);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, body.GetProperty("published_version_number").GetInt32());
    }

    [Fact]
    public async Task Publishing_by_the_same_actor_who_authored_the_draft_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (policyKey, version) = await CreateDraftAsync(ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/access-policies/{policyKey}/publish", AuthorId, new { version }), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("self_approval_forbidden", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Listing_shows_every_policy_created_in_the_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await CreateDraftAsync(ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, "/api/v1/access-policies", PublisherId), ct);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, body.GetProperty("data").GetArrayLength());
    }

    // --- BIO-ACC-001-QA-01: reads are protected too - a policy's content is itself the sensitive asset ---

    [Fact]
    public async Task Getting_a_policy_without_an_actor_at_all_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (policyKey, _) = await CreateDraftAsync(ct);

        var response = await _client.GetAsync($"/api/v1/access-policies/{policyKey}", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Getting_a_policy_without_a_provenance_proof_is_rejected_before_the_read_permission_is_ever_checked()
    {
        var ct = TestContext.Current.CancellationToken;
        var (policyKey, _) = await CreateDraftAsync(ct);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/access-policies/{policyKey}");
        request.Headers.Add(ActorResolver.ActorIdHeader, PublisherId); // deliberately no X-Actor-Provenance

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Listing_policies_without_an_actor_at_all_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await CreateDraftAsync(ct);

        var response = await _client.GetAsync("/api/v1/access-policies", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    // --- ADR-0006 Decision 3: provenance gate ---

    [Fact]
    public async Task Creating_a_draft_without_a_provenance_proof_is_rejected_before_authority_is_ever_checked()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/access-policies") { Content = JsonContent.Create(new { policy_key = "main-campus", rules = new[] { FullRule() } }) };
        request.Headers.Add(ActorResolver.ActorIdHeader, AuthorId); // deliberately no X-Actor-Provenance

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_provenance_proof_computed_for_a_different_actor_id_is_rejected_identically_to_a_missing_one()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/access-policies")
        { Content = JsonContent.Create(new { policy_key = "main-campus", rules = new[] { FullRule() } }) };
        request.Headers.Add(ActorResolver.ActorIdHeader, AuthorId);
        request.Headers.Add(ActorProvenanceGate.ProvenanceHeader, ComputeProvenanceProof("someone-else"));

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    // --- AC01: subject existence check at the HTTP boundary ---

    [Fact]
    public async Task Referencing_a_specific_subject_that_does_not_exist_is_rejected_with_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var rule = new
        {
            effect = "ALLOW", subject = new { kind = "SPECIFIC_PERSON", value = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99" },
            credential = new { kind = "ANY" }, location = new { kind = "ANY" }, schedule = new { kind = "ALWAYS" },
            identity_status = new { any_status = true }, card_status = new { any_status = true }, required_assurance_level = "BASIC",
        };

        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, "/api/v1/access-policies", AuthorId, new { policy_key = "unknown-subject", rules = new[] { rule } }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("subject_person_not_found", body.GetProperty("code").GetString());
    }

    // --- AC03/AC04: evaluation ---

    [Fact]
    public async Task Evaluating_against_a_published_policy_returns_a_decision_with_reason_and_matched_rule()
    {
        var ct = TestContext.Current.CancellationToken;
        var (policyKey, version) = await CreateDraftAsync(ct);
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/access-policies/{policyKey}/publish", PublisherId, new { version }), ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/access-policies/{policyKey}/evaluate", PublisherId, new
        {
            person_id = SubjectPersonId, credential_class = "staff-card", location_id = "main-gate", presented_card_status = "active", presented_assurance_level = "BASIC",
        }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("ALLOW", body.GetProperty("decision").GetString());
        Assert.Equal("allow_rule_matched", body.GetProperty("reason_code").GetString());
        Assert.Equal(1, body.GetProperty("matched_rule_id").GetInt32());
    }

    [Fact]
    public async Task Evaluating_against_a_policy_with_no_published_version_yet_denies_fail_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var (policyKey, _) = await CreateDraftAsync(ct); // never published

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/access-policies/{policyKey}/evaluate", AuthorId, new
        {
            person_id = SubjectPersonId, credential_class = "staff-card", location_id = "main-gate", presented_card_status = "active", presented_assurance_level = "BASIC",
        }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // a decision, never a 404
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("DENY", body.GetProperty("decision").GetString());
        Assert.Equal("no_published_policy", body.GetProperty("reason_code").GetString());
    }

    [Fact]
    public async Task Evaluating_without_a_provenance_proof_is_rejected_the_same_way_as_authoring()
    {
        var ct = TestContext.Current.CancellationToken;
        var (policyKey, _) = await CreateDraftAsync(ct);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/access-policies/{policyKey}/evaluate")
        {
            Content = JsonContent.Create(new { person_id = SubjectPersonId, credential_class = "staff-card", location_id = "main-gate", presented_card_status = "active", presented_assurance_level = "BASIC" }),
        };
        request.Headers.Add(ActorResolver.ActorIdHeader, AuthorId);

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }
}
