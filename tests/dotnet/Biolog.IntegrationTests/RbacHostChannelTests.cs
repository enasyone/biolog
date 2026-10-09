using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>
/// End-to-end proof, through the REAL packaged <c>Biolog.Api</c> host, that BIO-IDN-006's
/// existence-gateway wiring reads the real BIO-IDN-001 store and that AC02's tenant boundary and
/// AC03's separation-of-duties hold through the actual shipped endpoints - not merely an
/// injected test double. Mirrors every sibling story's own <c>*HostChannelTests.cs</c>: registers
/// and verifies a real domain through BIO-TEN-002's own endpoints, registers a real person
/// through BIO-IDN-001's own endpoint, then exercises BIO-IDN-006's endpoints through that
/// verified hostname.
///
/// <b>BIO-IDN-006-QA-08 (ADR-0006 Decision 3).</b> <see cref="ProvenanceSecret"/> is configured
/// via <c>ConfigureAppConfiguration</c> - the SAME mechanism a real deployment sets
/// <c>Rbac:ActorProvenanceSecret</c> through (an environment variable, in production), not a
/// test-only backdoor into RBAC's own internals; <see cref="ComputeProvenanceProof"/> models
/// exactly what an operator/deployment script (or, eventually, Emeka's real trusted edge) would
/// compute out of band. This project still has NO <c>InternalsVisibleTo</c> access to seed a
/// privileged role assignment directly (see this file's own QA01 test comment), so a valid
/// provenance proof alone is never enough to demonstrate a mutation SUCCEEDING here.
/// </summary>
public sealed class RbacHostChannelTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private const string TenantA = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string TenantB = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a88";
    private const string ProvenanceSecret = "real-host-e2e-provenance-secret-9f8e7d";

    public RbacHostChannelTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Rbac:ActorProvenanceSecret"] = ProvenanceSecret })));
    }

    private static string ComputeProvenanceProof(string actorId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ProvenanceSecret), Encoding.UTF8.GetBytes(actorId)));

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

    private static HttpRequestMessage ActorRequest(HttpMethod method, string url, string host, string actorId, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Host = host } };
        request.Headers.Add("X-Actor-Id", actorId);
        request.Headers.Add("X-Actor-Provenance", ComputeProvenanceProof(actorId));
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

    private async Task<string> RegisterPersonAsync(HttpClient client, string host, CancellationToken ct)
    {
        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, "/api/v1/persons", host, "admin_alice", new { first_name = "Ngozi", last_name = "Okafor" }), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.GetProperty("id").GetString()!;
    }

    /// <summary>
    /// BIO-IDN-006-QA-03 (Seyi round 2, Critical) extended BIO-IDN-006-QA-01's already-disclosed
    /// "no in-band SUPER_ADMIN bootstrap" precedent to EVERY role, not only SUPER_ADMIN - see
    /// <see cref="RoleAssignmentService"/>'s own header. This project deliberately has no
    /// seeding backdoor (see <see cref="BIOIDN006_QA01_PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_grant_SUPER_ADMIN_through_the_real_packaged_host"/>'s
    /// own comment), so it can no longer demonstrate a role assignment SUCCEEDING through the
    /// real host with an ordinary pre-auth actor - that capability was exactly the vulnerability
    /// this fix closes. What it proves instead: the request still reaches
    /// <see cref="RoleAssignmentService"/> (tenant/person resolution genuinely ran; this isn't a
    /// routing or tenant-context failure) and is rejected on authority grounds. The underlying
    /// AC01 existence-check and AC02 tenant-scoping proofs, exercised with a privileged actor,
    /// live at the unit level in <c>RoleAssignmentServiceTests.cs</c> and
    /// <c>PermissionServiceTests.cs</c>.
    /// </summary>
    [Fact]
    public async Task Assigning_a_role_through_a_verified_hostname_reaches_the_real_tenant_and_person_then_is_rejected_for_lack_of_authority()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "rbac-assign-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{personId}/roles/ENROLMENT", hostname, "admin_alice"), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BIOIDN006_assigning_a_role_for_an_unknown_canonical_person_id_is_rejected_for_lack_of_authority_before_existence_is_ever_checked()
    {
        // The authority gate runs before the existence gateway (an unauthorized caller learns
        // nothing about the target's state) - so this now fails the SAME way whether the person
        // exists or not. AC01's real existence-check proof (with a privileged actor) lives in
        // RoleAssignmentServiceTests.cs.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "rbac-unknown-person-e2e.example.edu.ng", TenantA, ct);
        const string neverRegisteredPersonId = "per_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a99";

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{neverRegisteredPersonId}/roles/ENROLMENT", hostname, "admin_alice"), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BIOIDN006_a_tenant_B_caller_gets_the_identical_rejection_as_the_wholly_unknown_case_never_leaking_tenant_A_state()
    {
        // Same gate-ordering consequence as the unknown-person case above: an unauthorized
        // caller in tenant B learns nothing about a real tenant A person's existence either -
        // the two cases are indistinguishable, which is itself the AC02 property this test now
        // proves at the HTTP boundary (the underlying tenant-scoped grant isolation is proven
        // directly in PermissionServiceTests.cs).
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostA = await RegisterAndVerifyDomainAsync(client, "rbac-real-tenant-a-e2e.example.edu.ng", TenantA, ct);
        var hostB = await RegisterAndVerifyDomainAsync(client, "rbac-real-tenant-b-e2e.example.edu.ng", TenantB, ct);
        var tenantAPersonId = await RegisterPersonAsync(client, hostA, ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{tenantAPersonId}/roles/ENROLMENT", hostB, "admin_alice"), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BIOIDN006_QA03_PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_assign_a_non_SUPER_ADMIN_role_through_the_real_packaged_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "rbac-qa03-role-assign-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        var assign = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{personId}/roles/TENANT_ADMIN", hostname, "unprivileged_attacker"), ct);
        var grant = await client.SendAsync(
            ActorRequest(HttpMethod.Put, "/api/v1/roles/TENANT_ADMIN/permissions/tenant.delete_all_data", hostname, "unprivileged_attacker", new { granted = true }), ct);

        Assert.Equal(HttpStatusCode.Forbidden, assign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode);
    }

    [Fact]
    public async Task BIOIDN006_QA04_PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_disable_a_maker_checker_rule_through_the_real_packaged_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "rbac-qa04-rule-manage-e2e.example.edu.ng", TenantA, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Put, "/api/v1/sensitive-actions/rbac.super_admin_assign/rule", hostname, "unprivileged_attacker",
            new { requires_second_approval = false }), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BIOIDN006_QA01_PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_grant_SUPER_ADMIN_through_the_real_packaged_host()
    {
        // Deliberately does NOT seed a bootstrap SUPER_ADMIN - IPersonRoleAssignmentRepository
        // is internal to the Rbac project (InternalsVisibleTo grants Biolog.Api and
        // Biolog.Features.Rbac.Tests only, not this integration-test project), so this project
        // has no seeding backdoor - exactly proving the point: the public API surface this test
        // exercises has no way to create a SUPER_ADMIN for ANY actor, privileged or not.
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "rbac-qa01-super-admin-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        var response = await client.SendAsync(ActorRequest(HttpMethod.Post, $"/api/v1/persons/{personId}/roles/SUPER_ADMIN", hostname, "unprivileged_attacker"), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("super_admin_mutation_requires_super_admin", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BIOIDN006_QA08_PRIVILEGE_ESCALATION_a_caller_without_a_valid_provenance_proof_is_rejected_before_authority_is_ever_checked_through_the_real_packaged_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "rbac-qa08-provenance-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/persons/{personId}/roles/SUPER_ADMIN") { Headers = { Host = hostname } };
        request.Headers.Add("X-Actor-Id", "unprivileged_attacker"); // deliberately no X-Actor-Provenance at all

        var response = await client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("actor_provenance_required", body.GetProperty("code").GetString()); // not super_admin_mutation_requires_super_admin
    }

    /// <summary>
    /// BIO-IDN-006-QA-07 (Seyi round 4, Critical) extended the "no in-band bootstrap" precedent
    /// (QA-01, then QA-03/QA-04 - see this file's own QA01 test comment) to the maker-checker
    /// WORKFLOW itself: raising or deciding a request now also requires real authority
    /// (<c>rbac.approval_request</c>/<c>rbac.approval_decide</c>), which this project has no
    /// seeding backdoor for. An unprivileged "registrar_ada" therefore now fails at the FIRST
    /// step (raising the request) rather than ever reaching the self-approval check this test
    /// used to isolate - self-approval's own real-host proof, with a privileged actor, lives in
    /// RbacEndpointsIntegrationTests.cs's own PRIVILEGE_ESCALATION_the_maker_cannot_approve_their_own_request_through_the_endpoint.
    /// </summary>
    [Fact]
    public async Task BIOIDN006_QA07_PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_raise_a_sensitive_action_approval_request_through_the_real_packaged_host()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();
        var hostname = await RegisterAndVerifyDomainAsync(client, "rbac-qa07-approval-request-e2e.example.edu.ng", TenantA, ct);
        var personId = await RegisterPersonAsync(client, hostname, ct);

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, $"/api/v1/sensitive-actions/person.archive/approval-requests/{personId}", hostname, "registrar_ada", new { reason = "archival sweep" }), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("permission_required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Assigning_a_role_through_an_unrecognised_host_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(ActorRequest(
            HttpMethod.Post, "/api/v1/persons/per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d/roles/ENROLMENT",
            "nobody-registered-this-rbac-host.example.edu.ng", "admin_alice"), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        Assert.Equal("tenant_context_missing", body.GetProperty("code").GetString());
    }
}
