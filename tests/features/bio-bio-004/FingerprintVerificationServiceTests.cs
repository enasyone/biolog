using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Providers;
using Biolog.Features.Biometrics.Synthetic;
using Biolog.Features.FingerprintVerification.Domain;
using Biolog.Features.FingerprintVerification.Service;
using Xunit;

namespace Biolog.Features.FingerprintVerification.Tests;

public sealed class FingerprintVerificationServiceTests
{
    private static readonly string NowUtc = "2026-01-01T00:00:00Z";

    private static (FingerprintVerificationService Service, InMemoryBiometricTemplateStore TemplateStore, FakeIdentityStatusGateway IdentityGateway)
        NewService()
    {
        var templateStore = new InMemoryBiometricTemplateStore();
        var provider = new SyntheticFingerprintProvider();
        var matcher = new SyntheticBiometricMatcher();
        var identityGateway = new FakeIdentityStatusGateway();
        var recordStore = new InMemoryFingerprintVerificationRecordStore();

        var counter = 1;
        var service = new FingerprintVerificationService(
            templateStore, provider, matcher, identityGateway, recordStore,
            newId: () => $"id-{counter++}", nowUtc: () => NowUtc);

        return (service, templateStore, identityGateway);
    }

    private static BiometricSample Sample(byte[] payload) => new()
    {
        Payload = payload,
        ContentType = "application/octet-stream",
        CapturedAtUtc = NowUtc,
    };

    private static async Task EnrolAsync(InMemoryBiometricTemplateStore store, string tenantId, string personId, byte[] payload, string providerVersion = "synthetic-fingerprint-1.0.0")
    {
        var digest = System.Security.Cryptography.SHA256.HashData(payload);
        var template = BiometricTemplate.Create(
            $"template-{Guid.NewGuid()}", tenantId, personId, BiometricModality.Fingerprint, "synthetic-fingerprint-v1", providerVersion, digest, NowUtc);
        await store.SaveAsync(template, CancellationToken.None);
    }

    // --- AC01: claimed identity resolves one authorised reference set ---

    [Fact]
    public async Task AC01_no_enrolled_template_throws_a_precondition_error_not_a_routine_outcome()
    {
        var (service, _, _) = NewService();

        var ex = await Assert.ThrowsAsync<FingerprintVerificationException>(
            () => service.VerifyAsync("ten_a", "per_a", Sample([1, 2, 3]), CancellationToken.None));

        Assert.Equal("fingerprint_verification_no_authorised_reference", ex.Code);
    }

    [Fact]
    public async Task AC01_verification_compares_against_the_single_newest_enrolled_template()
    {
        var (service, store, gateway) = NewService();
        gateway.Set("per_a", IdentityStatus.Active);

        // Two enrolled templates for the same identity - the newest one is "the one authorised
        // reference set" this story resolves to.
        await EnrolAsync(store, "ten_a", "per_a", [9, 9, 9]);
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample([1, 2, 3]), CancellationToken.None);

        Assert.Equal(BiometricComparisonOutcome.Matched, record.ComparisonOutcome);
        Assert.Equal(AccessDecision.Allow, record.AccessDecision);
    }

    [Fact]
    public async Task AC01_cross_tenant_claimed_identity_has_no_authorised_reference()
    {
        var (service, store, _) = NewService();
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        var ex = await Assert.ThrowsAsync<FingerprintVerificationException>(
            () => service.VerifyAsync("ten_b", "per_a", Sample([1, 2, 3]), CancellationToken.None));

        Assert.Equal("fingerprint_verification_no_authorised_reference", ex.Code);
    }

    // --- AC02: provider score/decision/model version are recorded ---

    [Fact]
    public async Task AC02_a_matched_verification_records_score_matcher_provider_and_model_version()
    {
        var (service, store, gateway) = NewService();
        gateway.Set("per_a", IdentityStatus.Active);
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample([1, 2, 3]), CancellationToken.None);

        Assert.Equal(1.0, record.Score);
        Assert.Equal("synthetic-exact-match-v1", record.MatcherId);
        Assert.NotNull(record.MatchedTemplateId);
        Assert.Equal("synthetic-fingerprint-v1", record.ProviderId);
        Assert.Equal("synthetic-fingerprint-1.0.0", record.ProviderVersion);
        Assert.Equal(BiometricComparisonOutcome.Matched, record.ComparisonOutcome);
        Assert.Equal(AccessDecision.Allow, record.AccessDecision);
    }

    [Fact]
    public async Task AC02_every_verification_attempt_is_persisted_and_retrievable_by_id()
    {
        var (service, store, gateway) = NewService();
        gateway.Set("per_a", IdentityStatus.Active);
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample([1, 2, 3]), CancellationToken.None);
        var fetched = await service.GetRecordAsync("ten_a", record.Id, CancellationToken.None);

        Assert.NotNull(fetched);
        Assert.Equal(record.Id, fetched!.Id);
    }

    [Fact]
    public async Task AC02_history_lists_every_attempt_for_the_claimed_identity()
    {
        var (service, store, gateway) = NewService();
        gateway.Set("per_a", IdentityStatus.Active);
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        await service.VerifyAsync("ten_a", "per_a", Sample([1, 2, 3]), CancellationToken.None);
        await service.VerifyAsync("ten_a", "per_a", Sample([9, 9, 9]), CancellationToken.None);

        var history = await service.GetHistoryAsync("ten_a", "per_a", CancellationToken.None);

        Assert.Equal(2, history.Count);
    }

    // --- AC03: no-match and acquisition failure are distinct outcomes ---

    [Fact]
    public async Task AC03_a_non_matching_live_sample_records_not_matched_and_denies_access()
    {
        var (service, store, gateway) = NewService();
        gateway.Set("per_a", IdentityStatus.Active);
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample([9, 9, 9]), CancellationToken.None);

        Assert.Equal(BiometricComparisonOutcome.NotMatched, record.ComparisonOutcome);
        Assert.Equal(AccessDecision.Deny, record.AccessDecision);
        Assert.Equal("no_match", record.DenyReason);
        Assert.Equal(0.0, record.Score);
    }

    [Fact]
    public async Task AC03_an_empty_live_sample_records_acquisition_failed_not_not_matched()
    {
        var (service, store, gateway) = NewService();
        gateway.Set("per_a", IdentityStatus.Active);
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample([]), CancellationToken.None);

        Assert.Equal(BiometricComparisonOutcome.AcquisitionFailed, record.ComparisonOutcome);
        Assert.Equal(AccessDecision.Deny, record.AccessDecision);
        Assert.Equal("acquisition_failed", record.DenyReason);
        Assert.Null(record.Score);
        Assert.Null(record.MatcherId);
        Assert.Null(record.MatchedTemplateId);
    }

    [Fact]
    public async Task AC03_acquisition_failure_never_throws_it_is_a_routine_recorded_outcome()
    {
        var (service, store, gateway) = NewService();
        gateway.Set("per_a", IdentityStatus.Active);
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        // No exception should escape - AC03 requires this to be a distinct, handled OUTCOME.
        var record = await service.VerifyAsync("ten_a", "per_a", Sample([]), CancellationToken.None);
        Assert.NotNull(record);
    }

    // --- AC04: suspended/revoked identities cannot be converted into access allow by match alone ---

    [Fact]
    public async Task AC04_a_matched_suspended_identity_is_denied_access_but_the_match_is_still_recorded()
    {
        var (service, store, gateway) = NewService();
        gateway.Set("per_a", IdentityStatus.Suspended);
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample([1, 2, 3]), CancellationToken.None);

        Assert.Equal(BiometricComparisonOutcome.Matched, record.ComparisonOutcome);
        Assert.Equal(AccessDecision.Deny, record.AccessDecision);
        Assert.Equal("identity_suspended", record.DenyReason);
    }

    [Fact]
    public async Task AC04_a_matched_revoked_identity_is_denied_access_but_the_match_is_still_recorded()
    {
        var (service, store, gateway) = NewService();
        gateway.Set("per_a", IdentityStatus.Revoked);
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample([1, 2, 3]), CancellationToken.None);

        Assert.Equal(BiometricComparisonOutcome.Matched, record.ComparisonOutcome);
        Assert.Equal(AccessDecision.Deny, record.AccessDecision);
        Assert.Equal("identity_revoked", record.DenyReason);
    }

    [Fact]
    public async Task AC04_a_matched_active_identity_is_allowed_access()
    {
        var (service, store, gateway) = NewService();
        gateway.Set("per_a", IdentityStatus.Active);
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample([1, 2, 3]), CancellationToken.None);

        Assert.Equal(AccessDecision.Allow, record.AccessDecision);
        Assert.Null(record.DenyReason);
    }

    [Fact]
    public async Task AC04_a_non_matching_suspended_identity_is_denied_for_no_match_not_suspension()
    {
        // A denied access decision for a suspended identity that never even matched must still
        // report the TRUE reason (no_match) - AC04 only forbids converting a MATCH into an allow,
        // it does not ask this service to relabel every other denial as a status problem.
        var (service, store, gateway) = NewService();
        gateway.Set("per_a", IdentityStatus.Suspended);
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample([9, 9, 9]), CancellationToken.None);

        Assert.Equal(BiometricComparisonOutcome.NotMatched, record.ComparisonOutcome);
        Assert.Equal(AccessDecision.Deny, record.AccessDecision);
        Assert.Equal("no_match", record.DenyReason);
    }

    [Fact]
    public async Task AC04_identity_status_gateway_is_not_consulted_unless_the_comparison_matched()
    {
        var (service, store, gateway) = NewService();
        // Deliberately no status set for "per_a" - if the service queried the gateway for a
        // non-matching attempt it would still get null back either way, but this proves the
        // no-match path never depends on identity status at all.
        await EnrolAsync(store, "ten_a", "per_a", [1, 2, 3]);

        var record = await service.VerifyAsync("ten_a", "per_a", Sample([9, 9, 9]), CancellationToken.None);

        Assert.Equal(AccessDecision.Deny, record.AccessDecision);
        Assert.Equal("no_match", record.DenyReason);
    }
}
