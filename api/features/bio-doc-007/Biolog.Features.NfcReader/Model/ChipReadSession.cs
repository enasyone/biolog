using Biolog.Features.DocumentClassifier.Model;

namespace Biolog.Features.NfcReader.Model;

/// <summary>
/// Orchestrates a full chip read against a caller-supplied <see cref="IChipDataGroupTransport"/>
/// (BIO-DOC-007-AC02/AC03/AC05): establish the secure channel with already-derived
/// <see cref="NfcAccessKeys"/>, then read every requested data group, tolerating individual data
/// groups that are simply not available (Dg3/Dg4 under Basic Access Control being the ordinary
/// case) without treating that as a read failure.
/// </summary>
public static class ChipReadSession
{
    public static ChipReadOutcome Read(
        IChipDataGroupTransport transport,
        NfcAccessKeys keys,
        IReadOnlyList<ChipDataGroupId> dataGroupsToRead,
        string documentSessionId,
        DocumentClassCandidate? classification = null,
        PassiveAuthenticationInputs? passiveAuthenticationInputs = null)
    {
        if (!transport.EstablishSecureChannel(keys))
        {
            return new ChipReadOutcome(
                documentSessionId,
                classification,
                chipCommunicationSucceeded: false,
                readFailureReason: "secure_channel_establishment_failed",
                dataGroupsRead: [],
                dataGroupsUnavailable: [],
                passiveAuthentication: PassiveAuthenticationResult.NotAttempted);
        }

        var read = new List<ChipDataGroupRead>();
        var unavailable = new List<ChipDataGroupId>();

        foreach (var id in dataGroupsToRead)
        {
            if (transport.TryReadDataGroup(id, out var rawBytes) && rawBytes is not null)
            {
                read.Add(ChipDataGroupRead.FromRawBytes(id, rawBytes));
            }
            else
            {
                unavailable.Add(id);
            }
        }

        var passiveAuthentication = passiveAuthenticationInputs is null
            ? PassiveAuthenticationResult.NotAttempted
            : PassiveAuthentication.Evaluate(
                read,
                passiveAuthenticationInputs.SodDeclaredHashes,
                passiveAuthenticationInputs.DocumentSignerCertificate,
                passiveAuthenticationInputs.TrustedCscaCertificates);

        return new ChipReadOutcome(
            documentSessionId,
            classification,
            chipCommunicationSucceeded: true,
            readFailureReason: null,
            dataGroupsRead: read,
            dataGroupsUnavailable: unavailable,
            passiveAuthentication: passiveAuthentication);
    }
}
