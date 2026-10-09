// Developer tests for BIO-CRD-005. SYNTHETIC fixtures only.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Biolog.Features.CardIssuance.Api;
using Biolog.Features.CardIssuance.Domain;
using Biolog.Features.CardIssuance.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.CardIssuance.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-CRD-005's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and endpoint groups'
/// <c>Map</c> methods directly, never through <c>Biolog.Api.Modules.BioCrd005Module</c> - this
/// project has no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>. Tenant context and the
/// person-existence gateway are fakes here; the real packaged pipeline (a genuine cross-story
/// dependency on BIO-IDN-001) is exercised separately in
/// <c>tests/dotnet/Biolog.IntegrationTests/CardIssuanceHostChannelTests.cs</c>.
/// </summary>
public sealed class CardIssueEndpointsIntegrationTests : IAsyncLifetime
{
    private const string TenantA = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string TenantB = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private IHost _host = null!;
    private HttpClient _client = null!;
    private FakeCurrentTenantProvider _tenantProvider = null!;

    public async ValueTask InitializeAsync()
    {
        _tenantProvider = new FakeCurrentTenantProvider(TenantA);

        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(_tenantProvider);
                services.AddSingleton<IPersonExistenceGateway>(new FakePersonExistenceGateway());
                services.AddSingleton<ICardIssueRepository, InMemoryCardIssueRepository>();
                services.AddSingleton<Audit.ICardIssueAuditSink, Audit.InMemoryCardIssueAuditSink>();
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<CardIssueLockRegistry>();
                services.AddScoped<CardIssueService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(CardIssueEndpoints.Map);
            });
        });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _host.StopAsync(CancellationToken.None);
        _host.Dispose();
    }

    private HttpRequestMessage IssueRequest(string personId = PersonId, string cardClass = CardClass) =>
        new(HttpMethod.Post, $"/api/v1/persons/{personId}/card-issues/{cardClass}")
        {
            Content = JsonContent.Create(new
            {
                template_ref = "template-1",
                photo_ref = "photo-1",
                logo_ref = "logo-1",
                signature_ref = "signature-1",
                printer_ref = "printer-1",
            }),
            Headers = { { "X-Actor-Id", "operator-1" } },
        };

    private static HttpRequestMessage StepRequest(HttpMethod method, string path, int? expectedVersion, string? idempotencyKey = null)
    {
        var message = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(new { expected_version = expectedVersion }),
        };
        message.Headers.Add("X-Actor-Id", "operator-1");
        if (idempotencyKey is not null)
        {
            message.Headers.Add(CardIssueEndpoints.IdempotencyKeyHeaderName, idempotencyKey);
        }

        return message;
    }

    [Fact]
    public async Task AC01_AC02_issuing_three_cards_yields_sequence_1_2_3_never_reused()
    {
        var first = await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        var second = await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        var third = await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var thirdBody = await third.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(1, firstBody.GetProperty("sequence_number").GetInt32());
        Assert.Equal("001", firstBody.GetProperty("sequence_display").GetString());
        Assert.Equal(2, secondBody.GetProperty("sequence_number").GetInt32());
        Assert.Equal(3, thirdBody.GetProperty("sequence_number").GetInt32());
    }

    [Fact]
    public async Task AC05_provenance_records_template_photo_logo_signature_operator_printer_and_time()
    {
        var response = await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var provenance = body.GetProperty("provenance");

        Assert.Equal("template-1", provenance.GetProperty("template_ref").GetString());
        Assert.Equal("photo-1", provenance.GetProperty("photo_ref").GetString());
        Assert.Equal("logo-1", provenance.GetProperty("logo_ref").GetString());
        Assert.Equal("signature-1", provenance.GetProperty("signature_ref").GetString());
        Assert.Equal("operator-1", provenance.GetProperty("operator_actor_id").GetString());
        Assert.Equal("user", provenance.GetProperty("operator_actor_type").GetString());
        Assert.Equal("printer-1", provenance.GetProperty("printer_ref").GetString());
        Assert.False(string.IsNullOrEmpty(provenance.GetProperty("issued_at").GetString()));
    }

    [Fact]
    public async Task AC03_encode_print_and_verify_are_independent_and_may_all_precede_activation()
    {
        var issued = await (await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var basePath = $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1";

        var encoded = await (await _client.SendAsync(StepRequest(HttpMethod.Post, $"{basePath}/encode", 1), TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(encoded.GetProperty("encoded_at").GetString()));

        var printed = await (await _client.SendAsync(StepRequest(HttpMethod.Post, $"{basePath}/print", 2), TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(printed.GetProperty("printed_at").GetString()));

        var verified = await (await _client.SendAsync(StepRequest(HttpMethod.Post, $"{basePath}/verify", 3), TestContext.Current.CancellationToken)).Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(verified.GetProperty("verified_at").GetString()));

        // All three precede activation, and activation still succeeds afterward (AC03: "may precede", not "must precede").
        var activateResponse = await _client.SendAsync(StepRequest(HttpMethod.Post, $"{basePath}/activate", 4, "idem-key-1"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        var activated = await activateResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("active", activated.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AC03_activation_succeeds_even_when_no_encode_print_or_verify_step_was_ever_recorded()
    {
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        var response = await _client.SendAsync(
            StepRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/activate", 1, "idem-key-2"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AC03_recording_the_same_step_twice_is_refused()
    {
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        var path = $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/encode";
        await _client.SendAsync(StepRequest(HttpMethod.Post, path, 1), TestContext.Current.CancellationToken);

        var second = await _client.SendAsync(StepRequest(HttpMethod.Post, path, 2), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("card_issue_step_already_recorded", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC04_activating_a_second_card_while_the_first_is_still_active_is_refused()
    {
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken); // sequence 1
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken); // sequence 2
        var basePath = $"/api/v1/persons/{PersonId}/card-issues/{CardClass}";

        var firstActivate = await _client.SendAsync(StepRequest(HttpMethod.Post, $"{basePath}/1/activate", 1, "idem-a"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, firstActivate.StatusCode);

        var secondActivate = await _client.SendAsync(StepRequest(HttpMethod.Post, $"{basePath}/2/activate", 1, "idem-b"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, secondActivate.StatusCode);
        var body = await secondActivate.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("another_card_already_active", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC04_deactivating_the_active_card_allows_a_new_one_to_be_activated()
    {
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken); // sequence 1
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken); // sequence 2
        var basePath = $"/api/v1/persons/{PersonId}/card-issues/{CardClass}";

        await _client.SendAsync(StepRequest(HttpMethod.Post, $"{basePath}/1/activate", 1, "idem-c"), TestContext.Current.CancellationToken);
        var deactivate = await _client.SendAsync(StepRequest(HttpMethod.Post, $"{basePath}/1/deactivate", 2), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var secondActivate = await _client.SendAsync(StepRequest(HttpMethod.Post, $"{basePath}/2/activate", 1, "idem-d"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, secondActivate.StatusCode);
    }

    [Fact]
    public async Task AC04_activation_requires_an_idempotency_key()
    {
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        var response = await _client.SendAsync(
            StepRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/activate", 1),
            TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("idempotency_key_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC04_replaying_the_same_activation_idempotency_key_returns_the_same_result_without_re_executing()
    {
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        var path = $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/activate";

        var first = await _client.SendAsync(StepRequest(HttpMethod.Post, path, 1, "idem-replay"), TestContext.Current.CancellationToken);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // Replayed with the SAME expected_version (same request fingerprint) and the SAME key.
        var replay = await _client.SendAsync(StepRequest(HttpMethod.Post, path, 1, "idem-replay"), TestContext.Current.CancellationToken);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstBody.GetProperty("version").GetInt32(), replayBody.GetProperty("version").GetInt32());
        Assert.Equal("active", replayBody.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AC04_reusing_an_idempotency_key_with_different_request_content_is_rejected()
    {
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        var path = $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/activate";

        await _client.SendAsync(StepRequest(HttpMethod.Post, path, 1, "idem-reuse"), TestContext.Current.CancellationToken);

        // Same key, different expected_version -> different fingerprint.
        var reused = await _client.SendAsync(StepRequest(HttpMethod.Post, path, 99, "idem-reuse"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        var body = await reused.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("idempotency_key_reuse", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Concurrent_activation_attempts_for_the_same_person_and_card_class_never_both_succeed()
    {
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken); // sequence 1
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken); // sequence 2
        var basePath = $"/api/v1/persons/{PersonId}/card-issues/{CardClass}";

        var attempt1 = _client.SendAsync(StepRequest(HttpMethod.Post, $"{basePath}/1/activate", 1, "idem-race-1"), TestContext.Current.CancellationToken);
        var attempt2 = _client.SendAsync(StepRequest(HttpMethod.Post, $"{basePath}/2/activate", 1, "idem-race-2"), TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(attempt1, attempt2);

        var successCount = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = results.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);
    }

    [Fact]
    public async Task A_stale_expected_version_is_rejected_as_a_concurrency_conflict()
    {
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        var path = $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/encode";
        await _client.SendAsync(StepRequest(HttpMethod.Post, path, 1), TestContext.Current.CancellationToken); // now version 2

        var stale = await _client.SendAsync(StepRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/print", 1), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("concurrency_conflict", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_absent_expected_version_is_rejected_with_precondition_required()
    {
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        var response = await _client.SendAsync(
            StepRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1/encode", expectedVersion: null),
            TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("precondition_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_card_issue_created_in_one_tenant_is_invisible_from_another_tenant()
    {
        _tenantProvider.TenantId = TenantA;
        await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);

        _tenantProvider.TenantId = TenantB;
        var crossTenantRead = await _client.GetAsync($"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantRead.StatusCode);

        // Issuing under the SAME person/card class from tenant B starts its OWN sequence at 1.
        var tenantBIssue = await _client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        var tenantBBody = await tenantBIssue.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal(1, tenantBBody.GetProperty("sequence_number").GetInt32());

        _tenantProvider.TenantId = TenantA;
        var tenantAStillIntact = await _client.GetFromJsonAsync<JsonElement>($"/api/v1/persons/{PersonId}/card-issues/{CardClass}/1", TestContext.Current.CancellationToken);
        Assert.Equal(1, tenantAStillIntact.GetProperty("sequence_number").GetInt32());
    }

    [Fact]
    public async Task Issuing_a_card_for_a_nonexistent_person_is_rejected()
    {
        var builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer();
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ICurrentTenantProvider>(new FakeCurrentTenantProvider(TenantA));
                services.AddSingleton<IPersonExistenceGateway>(new FakePersonExistenceGateway(new HashSet<string>()));
                services.AddSingleton<ICardIssueRepository, InMemoryCardIssueRepository>();
                services.AddSingleton<Audit.ICardIssueAuditSink, Audit.InMemoryCardIssueAuditSink>();
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<CardIssueLockRegistry>();
                services.AddScoped<CardIssueService>();
            });
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(CardIssueEndpoints.Map);
            });
        });

        using var host = await builder.StartAsync(TestContext.Current.CancellationToken);
        using var client = host.GetTestClient();

        var response = await client.SendAsync(IssueRequest(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("person_not_found", body.GetProperty("code").GetString());

        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Issuing_a_card_without_X_Actor_Id_is_rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/persons/{PersonId}/card-issues/{CardClass}")
        {
            Content = JsonContent.Create(new { template_ref = "t", printer_ref = "p" }),
        };
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }
}
