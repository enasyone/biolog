using Biolog.Features.Biometrics.Domain;
using Xunit;

namespace Biolog.Features.Biometrics.Tests;

/// <summary>BIO-BIO-001-AC02: "Provider-specific templates/identifiers are isolated." Proves
/// adversarially - by reflection, not just by inspection - that <see cref="BiometricTemplate"/>
/// carries no vendor/provider-internal identifier field.</summary>
public sealed class BiometricTemplateTests
{
    [Fact]
    public void AC02_BiometricTemplate_carries_no_vendor_or_provider_internal_identifier_field()
    {
        var propertyNames = typeof(BiometricTemplate).GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Vendor", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ProviderTemplateId", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ExternalId", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SessionHandle", StringComparison.OrdinalIgnoreCase));

        // Only the canonical shape crosses the boundary: id/tenant/person/modality/provider
        // metadata/opaque payload/timestamp - nothing else.
        Assert.Equal(
            new[] { "Id", "TenantId", "PersonId", "Modality", "ProviderId", "ProviderVersion", "Payload", "CreatedAtUtc" },
            propertyNames);
    }

    [Fact]
    public void AC02_ExtractedBiometricFeatures_from_a_provider_carries_no_identifier_of_any_kind()
    {
        var propertyNames = typeof(Providers.ExtractedBiometricFeatures).GetProperties().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "Payload", "ProviderVersion" }, propertyNames);
    }

    [Fact]
    public void Create_rejects_an_empty_payload()
    {
        Assert.Throws<ArgumentException>(() => BiometricTemplate.Create(
            "t1", "ten_a", "per_a", BiometricModality.Fingerprint, "prov", "1.0", Array.Empty<byte>(), "2026-01-01T00:00:00Z"));
    }

    [Fact]
    public void Create_rejects_a_blank_required_field()
    {
        Assert.Throws<ArgumentException>(() => BiometricTemplate.Create(
            "", "ten_a", "per_a", BiometricModality.Fingerprint, "prov", "1.0", new byte[] { 1 }, "2026-01-01T00:00:00Z"));
    }

    [Fact]
    public void Create_succeeds_with_valid_input_and_is_immutable()
    {
        var template = BiometricTemplate.Create(
            "t1", "ten_a", "per_a", BiometricModality.Face, "synthetic-face-v1", "1.0.0", new byte[] { 1, 2, 3 }, "2026-01-01T00:00:00Z");

        Assert.Equal("t1", template.Id);
        Assert.Equal(BiometricModality.Face, template.Modality);
        Assert.Equal(new byte[] { 1, 2, 3 }, template.Payload);

        // No public constructor - the only production entry point is Create(). Properties are
        // get-only (no init), so a `with { Prop = ... }` expression cannot alter any field
        // either - the type is immutable end to end, not just at construction.
        Assert.DoesNotContain(typeof(BiometricTemplate).GetConstructors(), c => c.IsPublic);
        Assert.All(typeof(BiometricTemplate).GetProperties(), p => Assert.Null(p.GetSetMethod(nonPublic: true)));
    }

    // BIO-BIO-001-QA-01 (Seyi round 1, High): Payload used to alias the caller's array on write
    // and the caller's copy on read, so a template could be tampered with post-enrolment without
    // any provider/store operation. Reproduces the reviewer's exact scenario in both directions.
    [Fact]
    public void QA01_mutating_the_array_passed_into_Create_does_not_change_the_stored_template()
    {
        var input = new byte[] { 1, 2, 3 };
        var template = BiometricTemplate.Create("t1", "ten_a", "per_a", BiometricModality.Fingerprint, "prov", "1.0", input, "2026-01-01T00:00:00Z");

        input[0] = 0xFF;

        Assert.Equal(new byte[] { 1, 2, 3 }, template.Payload);
    }

    [Fact]
    public void QA01_mutating_a_returned_Payload_array_does_not_change_the_template_or_a_second_read()
    {
        var template = BiometricTemplate.Create("t1", "ten_a", "per_a", BiometricModality.Fingerprint, "prov", "1.0", new byte[] { 1, 2, 3 }, "2026-01-01T00:00:00Z");

        var firstRead = template.Payload;
        firstRead[0] = 0xFF;
        var secondRead = template.Payload;

        Assert.Equal(new byte[] { 1, 2, 3 }, secondRead);
    }
}
