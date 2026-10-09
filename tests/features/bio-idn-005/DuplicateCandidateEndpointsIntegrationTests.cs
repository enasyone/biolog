using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.DuplicateReview.Api;
using Biolog.Features.DuplicateReview.Audit;
using Biolog.Features.DuplicateReview.Domain;
using Biolog.Features.DuplicateReview.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.DuplicateReview.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-IDN-005's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and
/// <see cref="DuplicateCandidateEndpoints.Map"/> directly, never through
/// <c>Biolog.Api.Modules.BioIdn005Module</c> - this project has no <c>ProjectReference</c> to
/// <c>Biolog.Api.csproj</c>. Tenant context and the two cross-feature gateways are fakes here;
/// the real packaged pipeline (including the real BIO-IDN-001/BIO-IDN-003-backed adapters and
/// AC04's cross-tenant proof) is covered by
/// <c>tests/dotnet/Biolog.IntegrationTests/DuplicateReviewHostChannelTests.cs</c>.
/// </summary>
public sealed class DuplicateCandidateEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonA = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PersonB = "per_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14";

    public async ValueTask InitializeAsync()
    {
        var persons = new List<PersonAttributes> { new(PersonA, "Ngozi", "Okafor", "2005-03-14"), new(PersonB, "Ngozi", "Okafor", "2005-03-14") };
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(new FakeCurrentTenantProvider(TenantId));
                services.AddSingleton<IDuplicateCandidateRepository, InMemoryDuplicateCandidateRepository>();
                services.AddSingleton<IDuplicateCandidateAuditSink, InMemoryDuplicateCandidateAuditSink>();
                services.AddSingleton<DuplicateCandidateLockRegistry>();
                services.AddSingleton<IPersonAttributeGateway>(new FakePersonAttributeGateway(persons));
                services.AddSingleton<IIdentifierAttributeGateway>(new FakeIdentifierAttributeGateway(new Dictionary<string, IReadOnlyList<IdentifierAttribute>>()));
                services.AddSingleton<IPersonContactPointRepository, InMemoryPersonContactPointRepository>();
                services.AddSingleton<PersonContactPointLockRegistry>();
                services.AddScoped<DuplicateReviewService>();
                services.AddScoped<PersonContactPointService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    DuplicateCandidateEndpoints.Map(endpoints);
                    PersonContactPointEndpoints.Map(endpoints);
                });
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

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(ActorResolver.ActorIdHeader, "registrar_ada");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: DuplicateCandidateJsonOptions.Strict);
        }

        return request;
    }

    [Fact]
    public async Task Scan_returns_200_with_a_raised_candidate_and_a_correlation_id_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonA}/duplicate-candidates/scan"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdHeader.HeaderName));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var data = body.GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());
        Assert.Equal("PENDING_REVIEW", data[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Scan_without_an_actor_header_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/persons/{PersonA}/duplicate-candidates/scan");

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Scan_for_an_unknown_person_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        const string unknownPersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b99";

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{unknownPersonId}/duplicate-candidates/scan"), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task List_returns_every_candidate_raised_for_this_person()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonA}/duplicate-candidates/scan"), ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{PersonA}/duplicate-candidates"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(1, body.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Decide_records_a_review_decision_and_returns_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var scan = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonA}/duplicate-candidates/scan"), ct);
        var scanBody = await scan.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = scanBody.GetProperty("data")[0].GetProperty("version").GetInt32();

        var response = await _client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{PersonA}/duplicate-candidates/{PersonB}/decisions",
            new { decision = "CONFIRMED_DUPLICATE", reason_code = "same_enrolment", version }), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("CONFIRMED_DUPLICATE", body.GetProperty("status").GetString());
        Assert.Equal("registrar_ada", body.GetProperty("decided_by_actor_id").GetString());
    }

    [Fact]
    public async Task Decide_without_a_version_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonA}/duplicate-candidates/scan"), ct);

        var response = await _client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{PersonA}/duplicate-candidates/{PersonB}/decisions",
            new { decision = "CONFIRMED_DUPLICATE", reason_code = "same_enrolment" }), ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Decide_on_a_pair_that_was_never_raised_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        const string strangerId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b88";

        var response = await _client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/persons/{PersonA}/duplicate-candidates/{strangerId}/decisions",
            new { decision = "CONFIRMED_DISTINCT", reason_code = "x", version = 1 }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Decide_rejects_an_unrecognised_JSON_field()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonA}/duplicate-candidates/scan"), ct);
        var request = ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonA}/duplicate-candidates/{PersonB}/decisions");
        request.Content = new StringContent(
            """{"decision":"CONFIRMED_DUPLICATE","reason_code":"x","version":1,"merge_now":true}""", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // --- BIO-IDN-005-QA-01: PersonContactPoint endpoints ---

    [Fact]
    public async Task ContactPoint_set_then_scan_raises_a_candidate_through_the_full_HTTP_pipeline()
    {
        var ct = TestContext.Current.CancellationToken;
        var setA = await _client.SendAsync(ActorRequest(HttpMethod.Put, $"/api/v1/persons/{PersonA}/contact-point", new { email = "shared@example.edu.ng" }), ct);
        Assert.Equal(HttpStatusCode.OK, setA.StatusCode);
        var setB = await _client.SendAsync(ActorRequest(HttpMethod.Put, $"/api/v1/persons/{PersonB}/contact-point", new { email = "shared@example.edu.ng" }), ct);
        Assert.Equal(HttpStatusCode.OK, setB.StatusCode);

        var scan = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonA}/duplicate-candidates/scan"), ct);

        var body = await scan.Content.ReadFromJsonAsync<JsonElement>(ct);
        var data = body.GetProperty("data");
        Assert.Equal(1, data.GetArrayLength());
        Assert.Contains("CONTACT_DATA", data[0].GetProperty("signal_sources").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task ContactPoint_get_returns_what_was_set_and_never_the_raw_unnormalized_input()
    {
        var ct = TestContext.Current.CancellationToken;
        await _client.SendAsync(ActorRequest(HttpMethod.Put, $"/api/v1/persons/{PersonA}/contact-point", new { email = "  Mixed.Case@Example.edu.NG  " }), ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{PersonA}/contact-point"), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("mixed.case@example.edu.ng", body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ContactPoint_get_when_nothing_is_on_file_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        const string neverSetPersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b77";

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{neverSetPersonId}/contact-point"), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ContactPoint_set_without_an_actor_header_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/persons/{PersonA}/contact-point")
        {
            Content = JsonContent.Create(new { email = "amara.eze@example.edu.ng" }, options: DuplicateCandidateJsonOptions.Strict),
        };

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ContactPoint_set_for_an_unknown_person_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        const string unknownPersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b66";

        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Put, $"/api/v1/persons/{unknownPersonId}/contact-point", new { email = "amara.eze@example.edu.ng" }), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());
    }
}
