using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.Rbac.Api;
using Biolog.Features.Rbac.Audit;
using Biolog.Features.Rbac.Domain;
using Biolog.Features.Rbac.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Biolog.Features.Rbac.Tests;

/// <summary>
/// Real HTTP pipeline tests for BIO-IDN-006's own endpoints, self-hosted via
/// <see cref="TestServer"/>. Wires this project's own DI registrations and all three endpoint
/// groups' <c>Map</c> methods directly, never through <c>Biolog.Api.Modules.BioIdn006Module</c>
/// - this project has no <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>. Tenant context and
/// the existence gateway are fakes here; the real packaged pipeline is covered by
/// <c>tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs</c>.
/// </summary>
public sealed class RbacEndpointsIntegrationTests : IAsyncLifetime
{
    private IHost _host = null!;
    private HttpClient _client = null!;
    private IPersonRoleAssignmentRepository _assignmentRepository = null!;
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    /// <summary>BIO-IDN-006-QA-08 (ADR-0006 Decision 3): the provenance secret this test host is configured with - every request in this file presents a matching HMAC proof via <see cref="ActorRequest"/>, so every OTHER test's assertions remain about actor-level authority, not provenance possession (that gate has its own dedicated tests below).</summary>
    private const string ProvenanceSecret = "test-provenance-secret-1a2b3c-32ok";

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
                services.AddSingleton<IPersonRoleAssignmentRepository, InMemoryPersonRoleAssignmentRepository>();
                services.AddSingleton<IRolePermissionGrantRepository, InMemoryRolePermissionGrantRepository>();
                services.AddSingleton<ISensitiveActionRuleRepository, InMemorySensitiveActionRuleRepository>();
                services.AddSingleton<IApprovalRequestRepository, InMemoryApprovalRequestRepository>();
                services.AddSingleton<IRbacAuditSink, InMemoryRbacAuditSink>();
                services.AddSingleton<RoleAssignmentLockRegistry>();
                services.AddSingleton<PermissionGrantLockRegistry>();
                services.AddSingleton<SensitiveActionRuleLockRegistry>();
                services.AddSingleton<ApprovalRequestLockRegistry>();
                services.AddSingleton<IPersonExistenceGateway>(new FakePersonExistenceGateway());
                services.AddScoped<RoleAssignmentService>();
                services.AddScoped<PermissionService>();
                services.AddScoped<MakerCheckerService>();
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
                app.UseEndpoints(endpoints =>
                {
                    RoleAssignmentEndpoints.Map(endpoints);
                    PermissionEndpoints.Map(endpoints);
                    MakerCheckerEndpoints.Map(endpoints);
                });
            });
        });

        _host = await builder.StartAsync(TestContext.Current.CancellationToken);
        _client = _host.GetTestClient();
        _assignmentRepository = _host.Services.GetRequiredService<IPersonRoleAssignmentRepository>();
    }

    /// <summary>Seeds a bootstrap SUPER_ADMIN directly via the repository, bypassing the service - the same out-of-band path production tenant provisioning would use (BIO-IDN-006-QA-01, see RoleAssignmentService's own header).</summary>
    private async Task<string> SeedBootstrapSuperAdminAsync(string bootstrapAdminPersonId, CancellationToken ct)
    {
        var now = Biolog.Features.Rbac.Identifiers.UtcInstant.Now();
        await _assignmentRepository.CreateAsync(
            new PersonRoleAssignment(TenantId, bootstrapAdminPersonId, RoleType.SuperAdmin) { Status = RoleAssignmentStatus.Active, Version = 1, CreatedAt = now, UpdatedAt = now },
            ct);
        return bootstrapAdminPersonId;
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
            request.Content = JsonContent.Create(body, options: RbacJsonOptions.Strict);
        }

        return request;
    }

    [Fact]
    public async Task AC01_the_role_catalogue_lists_all_eleven_baseline_roles()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/api/v1/roles", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal(11, body.GetProperty("data").GetArrayLength());
    }

    [Fact]
    public async Task Assigning_a_role_returns_200_with_the_active_assignment_and_a_correlation_id_header()
    {
        var ct = TestContext.Current.CancellationToken;
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync("per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a12", ct);
        var response = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/ENROLMENT", bootstrapAdmin), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdHeader.HeaderName));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("ACTIVE", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Assigning_a_role_without_an_actor_header_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/ENROLMENT"); // no X-Actor-Id, no provenance proof - the actor check runs first and rejects before provenance is ever considered

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_required", body.GetProperty("code").GetString());
    }

    // --- BIO-IDN-006-QA-08 (Zainab's security review, Critical; ADR-0006 Decision 3): a verified provenance proof is required, bound to the specific asserted actor id ---

    [Fact]
    public async Task Assigning_a_role_without_a_provenance_proof_is_rejected_even_for_an_otherwise_privileged_actor()
    {
        var ct = TestContext.Current.CancellationToken;
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync("per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a19", ct);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/ENROLMENT");
        request.Headers.Add(ActorResolver.ActorIdHeader, bootstrapAdmin); // no X-Actor-Provenance at all

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Assigning_a_role_with_a_provenance_proof_computed_for_a_different_actor_id_is_rejected_identically_to_a_missing_one()
    {
        // The proof is bound to the SPECIFIC asserted actor id, not an independent flag - a
        // valid proof for a DIFFERENT actor id must not satisfy this one.
        var ct = TestContext.Current.CancellationToken;
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync("per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a20", ct);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/ENROLMENT");
        request.Headers.Add(ActorResolver.ActorIdHeader, bootstrapAdmin);
        request.Headers.Add(ActorProvenanceGate.ProvenanceHeader, ComputeProvenanceProof("someone_else_entirely"));

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_provenance_gate_runs_before_the_authorization_check_a_caller_without_a_valid_proof_learns_nothing_about_actor_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/SUPER_ADMIN");
        request.Headers.Add(ActorResolver.ActorIdHeader, "unprivileged_attacker"); // would ALSO fail authority - that must never be what surfaces here

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString()); // not super_admin_mutation_requires_super_admin
    }

    [Fact]
    public async Task Deciding_an_approval_request_without_a_provenance_proof_is_also_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/sensitive-actions/person.archive/approval-requests/{PersonId}/1/decide");
        request.Headers.Add(ActorResolver.ActorIdHeader, "admin_bello");
        request.Content = JsonContent.Create(new { decision = "APPROVED", reason_code = "verified", version = 1 });

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ADVERSARIAL_unprivileged_asserted_actor_cannot_grant_SUPER_ADMIN_to_a_person()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/SUPER_ADMIN", "unprivileged_attacker"), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("super_admin_mutation_requires_super_admin", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ADVERSARIAL_unprivileged_actor_cannot_assign_TENANT_ADMIN_or_define_its_permissions()
    {
        var ct = TestContext.Current.CancellationToken;
        var assign = await _client.SendAsync(
            ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/TENANT_ADMIN", "unprivileged_attacker"), ct);
        var grant = await _client.SendAsync(
            ActorRequest(HttpMethod.Put, "/api/v1/roles/TENANT_ADMIN/permissions/tenant.delete_all_data", "unprivileged_attacker", new { granted = true }), ct);

        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
    }

    [Fact]
    public async Task ADVERSARIAL_unprivileged_actor_cannot_disable_a_maker_checker_rule()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(ActorRequest(
            HttpMethod.Put, "/api/v1/sensitive-actions/rbac.super_admin_assign/rule", "unprivileged_attacker",
            new { requires_second_approval = false }), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_legitimate_SUPER_ADMIN_can_grant_SUPER_ADMIN_through_the_full_pipeline()
    {
        var ct = TestContext.Current.CancellationToken;
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync("per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a10", ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/SUPER_ADMIN", bootstrapAdmin), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("ACTIVE", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Maker_checker_blocks_even_a_legitimate_SUPER_ADMIN_from_granting_SUPER_ADMIN_through_the_full_pipeline_when_configured()
    {
        var ct = TestContext.Current.CancellationToken;
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync("per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a11", ct);
        await _client.SendAsync(ActorRequest(
            HttpMethod.Put, "/api/v1/sensitive-actions/rbac.super_admin_assign/rule", bootstrapAdmin, new { requires_second_approval = true }), ct);

        var response = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/SUPER_ADMIN", bootstrapAdmin), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("second_approval_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Assigning_an_invalid_role_name_is_422()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/NOT_A_ROLE", "admin_alice"), ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Revoking_a_role_through_the_full_pipeline_then_listing_shows_it_revoked()
    {
        var ct = TestContext.Current.CancellationToken;
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync("per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a13", ct);
        var assigned = await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/ENROLMENT", bootstrapAdmin), ct);
        var assignedBody = await assigned.Content.ReadFromJsonAsync<JsonElement>(ct);
        var version = assignedBody.GetProperty("version").GetInt32();

        var revoke = await _client.SendAsync(ActorRequest(HttpMethod.Put, $"/api/v1/persons/{PersonId}/roles/ENROLMENT/revoke", bootstrapAdmin, new { version }), ct);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        var list = await _client.SendAsync(ActorRequest(HttpMethod.Get, $"/api/v1/persons/{PersonId}/roles", "admin_alice"), ct);
        var listBody = await list.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("REVOKED", listBody.GetProperty("data")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Setting_a_permission_grant_then_checking_it_through_the_full_pipeline_reflects_the_grant()
    {
        var ct = TestContext.Current.CancellationToken;
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync("per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a14", ct);
        await _client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{PersonId}/roles/ENROLMENT", bootstrapAdmin), ct);
        var setGrant = await _client.SendAsync(
            ActorRequest(HttpMethod.Put, "/api/v1/roles/ENROLMENT/permissions/enrolment.register", bootstrapAdmin, new { granted = true }), ct);
        Assert.Equal(HttpStatusCode.OK, setGrant.StatusCode);

        var check = await _client.GetAsync($"/api/v1/persons/{PersonId}/permissions/enrolment.register", ct);

        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var body = await check.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.True(body.GetProperty("granted").GetBoolean());
        Assert.Contains("ENROLMENT", body.GetProperty("via_roles").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task PRIVILEGE_ESCALATION_a_grant_can_never_be_set_for_SUPER_ADMIN_through_the_endpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.SendAsync(
            ActorRequest(HttpMethod.Put, "/api/v1/roles/SUPER_ADMIN/permissions/anything.at_all", "admin_alice", new { granted = false }), ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("super_admin_grant_not_configurable", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Maker_checker_full_pipeline_request_then_decide()
    {
        var ct = TestContext.Current.CancellationToken;
        var bootstrapAdmin = await SeedBootstrapSuperAdminAsync("per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a15", ct);
        var ruleSet = await _client.SendAsync(ActorRequest(
            HttpMethod.Put, "/api/v1/sensitive-actions/person.archive/rule", bootstrapAdmin, new { requires_second_approval = true }), ct);
        Assert.Equal(HttpStatusCode.OK, ruleSet.StatusCode);
        var maker = await SeedBootstrapSuperAdminAsync("per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a16", ct);
        var checker = await SeedBootstrapSuperAdminAsync("per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a17", ct);

        var requested = await _client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/sensitive-actions/person.archive/approval-requests/{PersonId}", maker, new { reason = "archival sweep" }), ct);
        Assert.Equal(HttpStatusCode.Created, requested.StatusCode);
        var requestedBody = await requested.Content.ReadFromJsonAsync<JsonElement>(ct);
        var sequenceNumber = requestedBody.GetProperty("sequence_number").GetInt32();
        var version = requestedBody.GetProperty("version").GetInt32();

        var decided = await _client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/sensitive-actions/person.archive/approval-requests/{PersonId}/{sequenceNumber}/decide", checker,
            new { decision = "APPROVED", reason_code = "verified", version }), ct);

        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        var decidedBody = await decided.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("APPROVED", decidedBody.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PRIVILEGE_ESCALATION_the_maker_cannot_approve_their_own_request_through_the_endpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        var actor = await SeedBootstrapSuperAdminAsync("per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a18", ct);
        var requested = await _client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/sensitive-actions/person.archive/approval-requests/{PersonId}", actor, new { reason = "archival sweep" }), ct);
        var requestedBody = await requested.Content.ReadFromJsonAsync<JsonElement>(ct);
        var sequenceNumber = requestedBody.GetProperty("sequence_number").GetInt32();
        var version = requestedBody.GetProperty("version").GetInt32();

        var response = await _client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/sensitive-actions/person.archive/approval-requests/{PersonId}/{sequenceNumber}/decide", actor,
            new { decision = "APPROVED", reason_code = "self-serve", version }), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("self_approval_forbidden", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rejects_an_unrecognised_JSON_field()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = ActorRequest(HttpMethod.Put, "/api/v1/roles/ENROLMENT/permissions/enrolment.register", "admin_alice");
        request.Content = new StringContent("""{"granted":true,"also_super_admin":true}""", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
