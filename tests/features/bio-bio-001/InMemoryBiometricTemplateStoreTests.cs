using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Synthetic;
using Xunit;

namespace Biolog.Features.Biometrics.Tests;

/// <summary>BIO-BIO-001-AC05/AC03: <see cref="InMemoryBiometricTemplateStore"/> is the local-dev
/// implementation of the persistence seam <see cref="Service.BiometricService"/> talks to instead
/// of a database.</summary>
public sealed class InMemoryBiometricTemplateStoreTests
{
    [Fact]
    public async Task GetTemplatesAsync_returns_empty_not_null_when_nothing_is_enrolled()
    {
        var store = new InMemoryBiometricTemplateStore();
        var result = await store.GetTemplatesAsync("ten_a", "per_a", BiometricModality.Fingerprint, CancellationToken.None);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SaveAsync_then_GetTemplatesAsync_returns_newest_first()
    {
        var store = new InMemoryBiometricTemplateStore();
        var t1 = Template("t1", "2026-01-01T00:00:00Z");
        var t2 = Template("t2", "2026-01-01T00:00:01Z");

        await store.SaveAsync(t1, CancellationToken.None);
        await store.SaveAsync(t2, CancellationToken.None);

        var result = await store.GetTemplatesAsync("ten_a", "per_a", BiometricModality.Fingerprint, CancellationToken.None);

        Assert.Equal(new[] { "t2", "t1" }, result.Select(t => t.Id));
    }

    [Fact]
    public async Task ADR0002_S4_3_templates_are_isolated_per_tenant_not_only_per_person()
    {
        var store = new InMemoryBiometricTemplateStore();
        await store.SaveAsync(Template("t1", "2026-01-01T00:00:00Z", tenantId: "ten_a"), CancellationToken.None);

        var fromOtherTenant = await store.GetTemplatesAsync("ten_b", "per_a", BiometricModality.Fingerprint, CancellationToken.None);

        Assert.Empty(fromOtherTenant);
    }

    // BIO-BIO-001-F02 (Zainab round 3, High): a delimiter-joined string key let two DIFFERENT
    // identity tuples collide when a component embedded the delimiter itself. Reproduces the
    // reviewer's exact tuples and proves each keeps its own, separate template list.
    [Fact]
    public async Task F02_embedded_delimiter_characters_in_tenant_or_person_id_never_collide_across_distinct_tuples()
    {
        var store = new InMemoryBiometricTemplateStore();
        var tupleOne = BiometricTemplate.Create(
            "t1", "a\0b", "c", BiometricModality.Fingerprint, "synthetic-fingerprint-v1", "1.0.0", new byte[] { 1, 2, 3 }, "2026-01-01T00:00:00Z");
        await store.SaveAsync(tupleOne, CancellationToken.None);

        // A DIFFERENT tenant/person tuple whose old delimiter-joined key ("a\0b\0c\0Fingerprint")
        // was identical to tupleOne's must never see tupleOne's template.
        var crossedTuple = await store.GetTemplatesAsync("a", "b\0c", BiometricModality.Fingerprint, CancellationToken.None);

        Assert.Empty(crossedTuple);
    }

    [Fact]
    public async Task Templates_are_isolated_per_modality()
    {
        var store = new InMemoryBiometricTemplateStore();
        await store.SaveAsync(Template("t1", "2026-01-01T00:00:00Z", modality: BiometricModality.Fingerprint), CancellationToken.None);

        var faceTemplates = await store.GetTemplatesAsync("ten_a", "per_a", BiometricModality.Face, CancellationToken.None);

        Assert.Empty(faceTemplates);
    }

    private static BiometricTemplate Template(
        string id, string createdAtUtc, string tenantId = "ten_a", BiometricModality modality = BiometricModality.Fingerprint) =>
        BiometricTemplate.Create(id, tenantId, "per_a", modality, "synthetic-fingerprint-v1", "1.0.0", new byte[] { 1, 2, 3 }, createdAtUtc);
}
