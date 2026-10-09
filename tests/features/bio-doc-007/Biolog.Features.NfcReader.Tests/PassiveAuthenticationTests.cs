using System.Security.Cryptography.X509Certificates;
using Biolog.Features.NfcReader.Model;
using Biolog.Features.NfcReader.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.NfcReader.Tests;

/// <summary>Developer tests for BIO-DOC-007-AC04 ("passive authentication/certificate-chain
/// results are explicit where supported").</summary>
public sealed class PassiveAuthenticationTests
{
    private static ChipDataGroupRead Dg1Read(byte[] rawBytes) => ChipDataGroupRead.FromRawBytes(ChipDataGroupId.Dg1, rawBytes);

    [Fact]
    public void AC04_matching_hashes_and_a_trusted_signer_chain_both_report_as_valid()
    {
        var (csca, dsc) = SyntheticCertificates.BuildTrustedChain();
        var dg1 = Dg1Read([1, 2, 3]);

        var result = PassiveAuthentication.Evaluate(
            readDataGroups: [dg1],
            sodDeclaredHashes: new Dictionary<ChipDataGroupId, string> { [ChipDataGroupId.Dg1] = dg1.Sha256Hash },
            documentSignerCertificate: dsc,
            trustedCscaCertificates: new X509Certificate2Collection(csca));

        Assert.True(result.Attempted);
        Assert.True(result.DataGroupHashesMatch);
        Assert.Empty(result.MismatchedDataGroups);
        Assert.True(result.CertificateChainValid);
        Assert.True(result.Passed);
    }

    [Fact]
    public void AC04_a_data_group_hash_that_does_not_match_the_sod_declared_hash_is_named_explicitly()
    {
        var (csca, dsc) = SyntheticCertificates.BuildTrustedChain();
        var dg1 = Dg1Read([1, 2, 3]);

        var result = PassiveAuthentication.Evaluate(
            readDataGroups: [dg1],
            sodDeclaredHashes: new Dictionary<ChipDataGroupId, string> { [ChipDataGroupId.Dg1] = "not-the-real-hash" },
            documentSignerCertificate: dsc,
            trustedCscaCertificates: new X509Certificate2Collection(csca));

        Assert.False(result.DataGroupHashesMatch);
        Assert.Equal([ChipDataGroupId.Dg1], result.MismatchedDataGroups);
        Assert.False(result.Passed);
        // The chain itself is still independently reported as valid - one failure never masks
        // the other (AC04's "explicit" requirement).
        Assert.True(result.CertificateChainValid);
    }

    [Fact]
    public void AC04_a_data_group_the_sod_never_declared_a_hash_for_is_a_mismatch_not_silently_ignored()
    {
        var (csca, dsc) = SyntheticCertificates.BuildTrustedChain();
        var dg1 = Dg1Read([1, 2, 3]);

        var result = PassiveAuthentication.Evaluate(
            readDataGroups: [dg1],
            sodDeclaredHashes: new Dictionary<ChipDataGroupId, string>(), // DG1 read, but SOD never mentioned it
            documentSignerCertificate: dsc,
            trustedCscaCertificates: new X509Certificate2Collection(csca));

        Assert.False(result.DataGroupHashesMatch);
        Assert.Equal([ChipDataGroupId.Dg1], result.MismatchedDataGroups);
    }

    [Fact]
    public void AC04_a_signer_certificate_not_chaining_to_any_trusted_csca_is_reported_invalid()
    {
        var (_, dsc) = SyntheticCertificates.BuildTrustedChain("Issuer");
        var (untrustedCsca, _) = SyntheticCertificates.BuildTrustedChain("Unrelated");
        var dg1 = Dg1Read([1, 2, 3]);

        var result = PassiveAuthentication.Evaluate(
            readDataGroups: [dg1],
            sodDeclaredHashes: new Dictionary<ChipDataGroupId, string> { [ChipDataGroupId.Dg1] = dg1.Sha256Hash },
            documentSignerCertificate: dsc,
            trustedCscaCertificates: new X509Certificate2Collection(untrustedCsca)); // wrong CSCA, not the one that signed dsc

        Assert.False(result.CertificateChainValid);
        Assert.NotEmpty(result.CertificateChainStatus);
        Assert.False(result.Passed);
        // Data group hashes matching is still reported independently, even though the chain failed.
        Assert.True(result.DataGroupHashesMatch);
    }

    [Fact]
    public void AC04_QA01_a_sod_declared_data_group_that_was_never_read_at_all_is_reported_missing_never_a_silent_pass()
    {
        // Seyi's exact reproduction: one group actually read (DG1, matching), one more declared in
        // the SOD but never read at all (DG2) - the chip simply omitted it. This must NOT report
        // as a hash match, and DG2 must be named explicitly as missing, not silently absent from
        // both MismatchedDataGroups and any other signal.
        var (csca, dsc) = SyntheticCertificates.BuildTrustedChain();
        var dg1 = Dg1Read([1, 2, 3]);

        var result = PassiveAuthentication.Evaluate(
            readDataGroups: [dg1],
            sodDeclaredHashes: new Dictionary<ChipDataGroupId, string>
            {
                [ChipDataGroupId.Dg1] = dg1.Sha256Hash,
                [ChipDataGroupId.Dg2] = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2", // declared, never read
            },
            documentSignerCertificate: dsc,
            trustedCscaCertificates: new X509Certificate2Collection(csca));

        Assert.False(result.DataGroupHashesMatch);
        Assert.Equal([ChipDataGroupId.Dg2], result.MissingDataGroups);
        Assert.Empty(result.MismatchedDataGroups); // DG1 (the one actually read) still matched cleanly
        Assert.False(result.Passed);
    }

    [Fact]
    public void AC04_QA03_result_collections_cannot_be_downcast_back_to_a_mutable_list()
    {
        var (csca, dsc) = SyntheticCertificates.BuildTrustedChain();
        var dg1 = Dg1Read([1, 2, 3]);

        var result = PassiveAuthentication.Evaluate(
            readDataGroups: [dg1],
            sodDeclaredHashes: new Dictionary<ChipDataGroupId, string> { [ChipDataGroupId.Dg1] = "wrong-hash" },
            documentSignerCertificate: dsc,
            trustedCscaCertificates: new X509Certificate2Collection(csca));

        // A caller downcasting any of these back to a mutable List<T> to erase evidence of a
        // mismatch/missing group/chain-status entry must fail (BIO-DOC-007-QA-03).
        Assert.IsNotType<List<ChipDataGroupId>>(result.MismatchedDataGroups);
        Assert.IsNotType<List<ChipDataGroupId>>(result.MissingDataGroups);
        Assert.IsNotType<List<string>>(result.CertificateChainStatus);
    }

    [Fact]
    public void AC04_not_attempted_is_a_distinct_state_from_a_failed_attempt()
    {
        var notAttempted = typeof(PassiveAuthenticationResult)
            .GetField("NotAttempted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as PassiveAuthenticationResult;

        Assert.NotNull(notAttempted);
        Assert.False(notAttempted!.Attempted);
        Assert.False(notAttempted.Passed);
    }
}
