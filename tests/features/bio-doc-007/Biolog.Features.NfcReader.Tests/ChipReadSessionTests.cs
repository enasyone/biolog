using System.Security.Cryptography.X509Certificates;
using Biolog.Features.NfcReader.Model;
using Biolog.Features.NfcReader.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.NfcReader.Tests;

/// <summary>Developer tests for BIO-DOC-007-AC02 (standards-focused data-group reading), AC03
/// (cross-linked to the document session) and AC05 (read failure distinct from authenticity
/// failure).</summary>
public sealed class ChipReadSessionTests
{
    private static NfcAccessKeys SampleKeys() => ChipAccessKeyDerivation.DeriveKeys(
        NfcAccessKeySeedMaterial.CardAccessNumber.Create("123456"));

    [Fact]
    public void AC02_reads_every_requested_data_group_that_is_available()
    {
        var transport = new FakeChipDataGroupTransport(
            secureChannelSucceeds: true,
            dataGroups: new Dictionary<ChipDataGroupId, byte[]>
            {
                [ChipDataGroupId.Dg1] = [1, 2, 3],
                [ChipDataGroupId.Dg2] = [4, 5, 6, 7],
            });

        var outcome = ChipReadSession.Read(
            transport, SampleKeys(), [ChipDataGroupId.Dg1, ChipDataGroupId.Dg2], "session-1");

        Assert.Equal(2, outcome.DataGroupsRead.Count);
        Assert.Contains(outcome.DataGroupsRead, r => r.Id == ChipDataGroupId.Dg1 && r.LengthBytes == 3);
        Assert.Contains(outcome.DataGroupsRead, r => r.Id == ChipDataGroupId.Dg2 && r.LengthBytes == 4);
    }

    [Fact]
    public void AC02_a_data_group_that_is_not_present_is_reported_unavailable_not_treated_as_a_failure()
    {
        var transport = new FakeChipDataGroupTransport(
            secureChannelSucceeds: true,
            dataGroups: new Dictionary<ChipDataGroupId, byte[]> { [ChipDataGroupId.Dg1] = [1] });

        var outcome = ChipReadSession.Read(
            transport, SampleKeys(), [ChipDataGroupId.Dg1, ChipDataGroupId.Dg3], "session-1");

        Assert.True(outcome.ChipCommunicationSucceeded);
        Assert.Single(outcome.DataGroupsRead);
        Assert.Equal([ChipDataGroupId.Dg3], outcome.DataGroupsUnavailable);
    }

    [Fact]
    public void AC03_the_outcome_carries_the_document_session_id_it_was_read_for()
    {
        var transport = new FakeChipDataGroupTransport(true, new Dictionary<ChipDataGroupId, byte[]>());

        var outcome = ChipReadSession.Read(transport, SampleKeys(), [], "doc-session-42");

        Assert.Equal("doc-session-42", outcome.DocumentSessionId);
    }

    [Fact]
    public void AC05_a_failed_secure_channel_is_reported_as_a_read_failure_with_no_data_groups_and_no_authenticity_claim()
    {
        var transport = new FakeChipDataGroupTransport(
            secureChannelSucceeds: false,
            dataGroups: new Dictionary<ChipDataGroupId, byte[]> { [ChipDataGroupId.Dg1] = [1] });

        var outcome = ChipReadSession.Read(
            transport, SampleKeys(), [ChipDataGroupId.Dg1], "session-1");

        Assert.False(outcome.ChipCommunicationSucceeded);
        Assert.Equal("secure_channel_establishment_failed", outcome.ReadFailureReason);
        Assert.Empty(outcome.DataGroupsRead);
        Assert.Empty(outcome.DataGroupsUnavailable);
        // A read failure says nothing about authenticity - it does not even attempt passive
        // authentication (nothing was read to authenticate).
        Assert.False(outcome.PassiveAuthentication.Attempted);

        // Structural proof of AC05: no property on this type is a bare bool that could conflate
        // read-success with authenticity - the only authenticity-shaped data lives inside the
        // separate, distinctly-typed PassiveAuthenticationResult sub-object.
        var boolPropertyNames = typeof(ChipReadOutcome)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(bool))
            .Select(p => p.Name)
            .ToList();
        Assert.Equal(["ChipCommunicationSucceeded"], boolPropertyNames);
    }

    [Fact]
    public void AC05_a_successful_read_still_says_nothing_about_authenticity()
    {
        var transport = new FakeChipDataGroupTransport(true, new Dictionary<ChipDataGroupId, byte[]> { [ChipDataGroupId.Dg1] = [1] });

        var outcome = ChipReadSession.Read(transport, SampleKeys(), [ChipDataGroupId.Dg1], "session-1");

        Assert.True(outcome.ChipCommunicationSucceeded);
        Assert.Null(outcome.ReadFailureReason);
    }

    [Fact]
    public void AC04_without_passive_authentication_inputs_the_outcome_explicitly_reports_not_attempted()
    {
        var transport = new FakeChipDataGroupTransport(true, new Dictionary<ChipDataGroupId, byte[]> { [ChipDataGroupId.Dg1] = [1] });

        var outcome = ChipReadSession.Read(transport, SampleKeys(), [ChipDataGroupId.Dg1], "session-1");

        Assert.False(outcome.PassiveAuthentication.Attempted);
        Assert.False(outcome.PassiveAuthentication.Passed);
    }

    [Fact]
    public void AC04_supplying_passive_authentication_inputs_runs_it_as_part_of_the_same_read()
    {
        var dg1Bytes = new byte[] { 1, 2, 3 };
        var transport = new FakeChipDataGroupTransport(true, new Dictionary<ChipDataGroupId, byte[]> { [ChipDataGroupId.Dg1] = dg1Bytes });
        var expectedHash = ChipDataGroupRead.FromRawBytes(ChipDataGroupId.Dg1, dg1Bytes).Sha256Hash;
        var (csca, dsc) = SyntheticCertificates.BuildTrustedChain();

        var outcome = ChipReadSession.Read(
            transport, SampleKeys(), [ChipDataGroupId.Dg1], "session-1",
            passiveAuthenticationInputs: new PassiveAuthenticationInputs(
                new Dictionary<ChipDataGroupId, string> { [ChipDataGroupId.Dg1] = expectedHash },
                dsc,
                new X509Certificate2Collection(csca)));

        Assert.True(outcome.PassiveAuthentication.Attempted);
        Assert.True(outcome.PassiveAuthentication.Passed);
    }

    [Fact]
    public void AC02_QA03_DataGroupsRead_cannot_be_downcast_back_to_a_mutable_list()
    {
        var transport = new FakeChipDataGroupTransport(true, new Dictionary<ChipDataGroupId, byte[]> { [ChipDataGroupId.Dg1] = [1, 2, 3] });

        var outcome = ChipReadSession.Read(transport, SampleKeys(), [ChipDataGroupId.Dg1], "session-1");

        // A caller downcasting the exposed collection back to a mutable List<T> to clear/alter
        // chip evidence after the fact must fail - the stored collection is a genuinely distinct,
        // non-castable type, not merely an interface view over a mutable list (BIO-DOC-007-QA-03).
        Assert.IsNotType<List<ChipDataGroupRead>>(outcome.DataGroupsRead);
        Assert.Single(outcome.DataGroupsRead); // unaffected by any attempted downcast
    }

    [Fact]
    public void AC02_QA03_DataGroupsUnavailable_cannot_be_downcast_back_to_a_mutable_list()
    {
        var transport = new FakeChipDataGroupTransport(true, new Dictionary<ChipDataGroupId, byte[]>());

        var outcome = ChipReadSession.Read(transport, SampleKeys(), [ChipDataGroupId.Dg3], "session-1");

        Assert.IsNotType<List<ChipDataGroupId>>(outcome.DataGroupsUnavailable);
    }
}
