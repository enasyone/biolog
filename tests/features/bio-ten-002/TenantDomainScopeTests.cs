using System.Text.Json;
using Biolog.Features.TenantDomainRegistry.Api;
using Xunit;

namespace Biolog.Features.TenantDomainRegistry.Tests;

/// <summary>
/// BIO-TEN-002-AC01 (TenantDomain stores exactly hostname/tenant/status/primary flag, plus
/// versioning metadata) and AC05 (A-record/CNAME deployment detail is separated from
/// application routing). Both are enforced structurally, by omission - these tests are the
/// adversarial proof that the omission actually holds, not merely assumed.
/// </summary>
public sealed class TenantDomainScopeTests
{
    private static readonly string[] DnsDeploymentFieldNames =
    [
        "a_record", "arecord", "cname", "cname_target", "dns_target", "ip_address",
        "ip", "target_ip", "record_type", "ttl", "zone", "dns_record",
    ];

    [Fact]
    public void RegisterDomainRequest_has_no_dns_deployment_fields()
    {
        var properties = typeof(RegisterDomainRequest).GetProperties().Select(p => p.Name.ToLowerInvariant());
        Assert.Empty(properties.Intersect(DnsDeploymentFieldNames));
    }

    [Fact]
    public void TenantDomainResponse_has_no_dns_deployment_fields()
    {
        var properties = typeof(TenantDomainResponse).GetProperties().Select(p => p.Name.ToLowerInvariant());
        Assert.Empty(properties.Intersect(DnsDeploymentFieldNames));
    }

    [Fact]
    public void Domain_TenantDomain_has_no_dns_deployment_fields()
    {
        var properties = typeof(Domain.TenantDomain).GetProperties().Select(p => p.Name.ToLowerInvariant());
        Assert.Empty(properties.Intersect(DnsDeploymentFieldNames));
    }

    [Theory]
    [InlineData("a_record")]
    [InlineData("cname")]
    [InlineData("ip_address")]
    [InlineData("credential_secret")] // adversarial: not a DNS field, but also not a control-plane field this story owns
    public async Task Register_rejects_a_smuggled_unrecognised_field(string smuggledField)
    {
        var payload = $$"""{"hostname":"portal.example.edu.ng","tenant_id":"ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77","{{smuggledField}}":"anything"}""";

        var ex = await Record.ExceptionAsync(async () =>
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
            await JsonSerializer.DeserializeAsync<RegisterDomainRequest>(stream, TenantDomainJsonOptions.Strict, TestContext.Current.CancellationToken);
        });

        Assert.IsType<JsonException>(ex);
    }

    [Fact]
    public void RegisterDomainRequest_has_exactly_hostname_and_tenant_id()
    {
        var names = typeof(RegisterDomainRequest).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["Hostname", "TenantId"], names);
    }

    [Fact]
    public void TenantDomainResponse_has_exactly_the_schema_fields_plus_versioning_metadata()
    {
        var names = typeof(TenantDomainResponse).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["CreatedAt", "Hostname", "IsPrimary", "Status", "TenantId", "UpdatedAt", "Version"], names);
    }
}
