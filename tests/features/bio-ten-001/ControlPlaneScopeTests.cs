using System.Text;
using System.Text.Json;
using Biolog.Features.TenantRegistry.Api;
using Biolog.Features.TenantRegistry.Domain;
using Xunit;

namespace Biolog.Features.TenantRegistry.Tests;

/// <summary>
/// BIO-TEN-001-AC01 (control-plane scoped) and AC02 (no operational person/credential data
/// in a control-plane table). AC01's negative case is structural, not a runtime check: see
/// <see cref="Tenant_never_carries_a_tenant_id_field"/> and the reflection assertion in
/// <see cref="Tenant_type_has_no_tenant_id_property"/> - there is no field on the aggregate a
/// bug could forget to filter by, because there is nothing to filter.
/// </summary>
public sealed class ControlPlaneScopeTests
{
    [Fact]
    public void Tenant_type_has_no_tenant_id_property()
    {
        // The versioned-resource envelope (ADR-0001 §3.1) requires tenant_id on every
        // TENANT-PLANE mutable aggregate. Tenant is the control-plane boundary itself
        // (identifier-registry.json: plane "control") and must never carry one - if it
        // did, some later story could plausibly try to "filter tenants by tenant" and the
        // type system would let it.
        var properties = typeof(Tenant).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain("TenantId", properties);
    }

    [Fact]
    public void Tenant_never_carries_a_tenant_id_field()
    {
        var tenant = new Tenant
        {
            Id = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77",
            Name = "Synthetic Tenant",
            Status = TenantStatus.Active,
            LicensePlan = "starter",
            LicenseSeats = 10,
            RoutingRegion = "eu-west",
            Version = 1,
            CreatedAt = "2026-09-21T00:00:00.000Z",
            UpdatedAt = "2026-09-21T00:00:00.000Z",
        };

        var json = JsonSerializer.Serialize(tenant);
        Assert.DoesNotContain("tenant_id", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("date_of_birth")]
    [InlineData("email")]
    [InlineData("biometric_template")]
    [InlineData("credential_secret")]
    [InlineData("national_id")]
    [InlineData("password")]
    public void CreateTenantRequest_deserialization_rejects_operational_or_secret_shaped_fields(string smuggledField)
    {
        var payload = $$"""
            {
              "name": "Acme University",
              "license_plan": "enterprise",
              "license_seats": 100,
              "routing_region": "eu-west",
              "{{smuggledField}}": "should never be accepted here"
            }
            """;

        var ex = Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize<CreateTenantRequest>(payload, TenantJsonOptions.Strict));

        Assert.NotNull(ex);
    }

    [Fact]
    public void CreateTenantRequest_accepts_exactly_the_documented_control_plane_fields()
    {
        const string payload = """
            {
              "name": "Acme University",
              "status": "active",
              "license_plan": "enterprise",
              "license_seats": 100,
              "routing_region": "eu-west"
            }
            """;

        var request = JsonSerializer.Deserialize<CreateTenantRequest>(payload, TenantJsonOptions.Strict);

        Assert.NotNull(request);
        Assert.Equal("Acme University", request!.Name);
    }

    [Fact]
    public void UpdateTenantRequest_deserialization_rejects_an_unrecognised_field()
    {
        const string payload = """
            { "version": 1, "ssn": "000-00-0000" }
            """;

        Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateTenantRequest>(payload, TenantJsonOptions.Strict));
    }
}
