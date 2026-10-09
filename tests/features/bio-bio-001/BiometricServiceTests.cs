using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;
using Biolog.Features.Biometrics.Service;
using Biolog.Features.Biometrics.Synthetic;
using Xunit;

namespace Biolog.Features.Biometrics.Tests;

/// <summary>BIO-BIO-001-AC03: "Biometric service is separate from MySQL matching logic." Proves
/// the separation both structurally (the service depends only on interfaces) and behaviourally
/// (enrol/verify orchestration works end to end against synthetic implementations, AC05).</summary>
public sealed class BiometricServiceTests
{
    private const string TenantA = "ten_a";
    private const string TenantB = "ten_b";
    private const string PersonA = "per_a";

    private static BiometricService NewService(
        out InMemoryBiometricTemplateStore store, out SyntheticBiometricMatcher matcher)
    {
        store = new InMemoryBiometricTemplateStore();
        matcher = new SyntheticBiometricMatcher();
        return new BiometricService(new SyntheticFingerprintProvider(), new SyntheticFaceProvider(), store, matcher);
    }

    [Fact]
    public void AC03_BiometricService_has_no_dependency_of_any_kind_on_a_SQL_or_MySQL_client()
    {
        var constructorParameterTypes = typeof(BiometricService)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType.FullName ?? p.ParameterType.Name);

        Assert.DoesNotContain(constructorParameterTypes, name =>
            name.Contains("Sql", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("MySql", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DbConnection", StringComparison.OrdinalIgnoreCase));

        // Only the four provider-neutral interfaces (plus two optional injectable functions for
        // id/clock) - never a concrete store, matcher or database type.
        var ctor = typeof(BiometricService).GetConstructors().Single();
        Assert.True(ctor.GetParameters()[0].ParameterType.IsInterface);
        Assert.True(ctor.GetParameters()[1].ParameterType.IsInterface);
        Assert.True(ctor.GetParameters()[2].ParameterType.IsInterface);
        Assert.True(ctor.GetParameters()[3].ParameterType.IsInterface);
    }

    [Fact]
    public async Task AC01_AC02_AC03_enrol_then_verify_round_trips_through_the_provider_and_store_seams()
    {
        var service = NewService(out _, out _);
        var sample = Sample(new byte[] { 7, 7, 7 });

        var enrolled = await service.EnrollAsync(TenantA, PersonA, BiometricModality.Fingerprint, sample, CancellationToken.None);
        Assert.Equal(TenantA, enrolled.TenantId);
        Assert.Equal(PersonA, enrolled.PersonId);

        var result = await service.VerifyAsync(TenantA, PersonA, BiometricModality.Fingerprint, sample, CancellationToken.None);

        Assert.True(result.IsMatch);
        Assert.Equal(enrolled.Id, result.MatchedTemplateId);
    }

    [Fact]
    public async Task Verify_with_a_different_sample_does_not_match()
    {
        var service = NewService(out _, out _);
        await service.EnrollAsync(TenantA, PersonA, BiometricModality.Fingerprint, Sample(new byte[] { 1 }), CancellationToken.None);

        var result = await service.VerifyAsync(TenantA, PersonA, BiometricModality.Fingerprint, Sample(new byte[] { 2 }), CancellationToken.None);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task Verify_with_nothing_enrolled_throws_a_typed_not_found_error()
    {
        var service = NewService(out _, out _);

        var ex = await Assert.ThrowsAsync<BiometricServiceException>(
            () => service.VerifyAsync(TenantA, PersonA, BiometricModality.Fingerprint, Sample(new byte[] { 1 }), CancellationToken.None));

        Assert.Equal("biometric_template_not_found", ex.Code);
    }

    [Fact]
    public async Task ADR0002_S4_3_a_person_enrolled_in_one_tenant_has_no_templates_visible_from_another_tenant()
    {
        var service = NewService(out _, out _);
        await service.EnrollAsync(TenantA, PersonA, BiometricModality.Face, Sample(new byte[] { 5 }), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<BiometricServiceException>(
            () => service.VerifyAsync(TenantB, PersonA, BiometricModality.Face, Sample(new byte[] { 5 }), CancellationToken.None));

        Assert.Equal("biometric_template_not_found", ex.Code);
    }

    // BIO-BIO-001-QA-01 (Seyi round 1, High): reproduces the reviewer's exact scenario -
    // enrol, mutate the returned template's Payload, then read the template back from the store
    // and confirm the stored candidate is unaffected (previously it changed, because the
    // returned array and the stored instance's array were the same object).
    [Fact]
    public async Task QA01_mutating_an_enrolled_templates_Payload_after_the_fact_does_not_alter_the_stored_candidate()
    {
        var service = NewService(out var store, out _);
        var enrolled = await service.EnrollAsync(TenantA, PersonA, BiometricModality.Fingerprint, Sample(new byte[] { 1, 2, 3 }), CancellationToken.None);
        var originalPayload = (byte[])enrolled.Payload.Clone();

        enrolled.Payload[0] = 0xFF;

        var stored = await store.GetTemplatesAsync(TenantA, PersonA, BiometricModality.Fingerprint, CancellationToken.None);
        Assert.Equal(originalPayload, stored[0].Payload);
    }

    [Fact]
    public async Task Verify_uses_the_most_recently_enrolled_template_when_a_person_re_enrols()
    {
        var service = NewService(out _, out _);
        await service.EnrollAsync(TenantA, PersonA, BiometricModality.Fingerprint, Sample(new byte[] { 1 }), CancellationToken.None);
        await service.EnrollAsync(TenantA, PersonA, BiometricModality.Fingerprint, Sample(new byte[] { 2 }), CancellationToken.None);

        var result = await service.VerifyAsync(TenantA, PersonA, BiometricModality.Fingerprint, Sample(new byte[] { 2 }), CancellationToken.None);

        Assert.True(result.IsMatch);
    }

    private static Providers.BiometricSample Sample(byte[] payload) => new()
    {
        Payload = payload,
        ContentType = "application/octet-stream",
        CapturedAtUtc = "2026-01-01T00:00:00Z",
    };
}
