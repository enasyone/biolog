using Biolog.Features.NfcReader.Model;

namespace Biolog.Features.EvidenceComparison.Model;

/// <summary>
/// NFC's contribution to a cross-comparison (BIO-DOC-008-AC01/AC03) - deliberately NOT a decoded
/// field value alongside MRZ/barcode/visual. BIO-DOC-007's <c>ChipDataGroupRead</c> exposes only a
/// SHA-256 hash of each data group's raw bytes, never the decoded content (Dg1/Dg2 are identity
/// PII, ADR-0003 Decision 5) - by the time a caller holds a <c>ChipReadOutcome</c>, the raw MRZ
/// bytes DG1 would carry have already been discarded. Decoding a DG1 hash back into a comparable
/// MRZ string is not possible (it is a one-way hash), and reconstructing DG1's exact ICAO 9303
/// Part 10 TLV/ASN.1 byte encoding from a candidate MRZ string to re-hash and compare is a
/// document-forensics-grade encoding task out of this story's scope.
///
/// <para>
/// Instead, NFC contributes an AUTHENTICITY/AVAILABILITY signal: did a chip read even happen, did
/// it succeed, and did passive authentication pass. This is genuinely useful to a cross-comparison
/// - a document whose MRZ/barcode/visual fields all agree with each other but whose chip failed
/// passive authentication is still a real concern <see cref="CrossComparisonResult.RequiresManualReview"/>
/// surfaces - without pretending this story can compare decoded chip field values it structurally
/// cannot obtain.
/// </para>
/// </summary>
public sealed record NfcCorroboration
{
    public bool Available { get; internal init; }
    public bool CommunicationSucceeded { get; internal init; }

    /// <summary>Null when passive authentication was never attempted (no chip communication, or
    /// the caller never supplied SOD/certificate material to BIO-DOC-007) - never coerced into
    /// true or false.</summary>
    public bool? PassiveAuthenticationPassed { get; internal init; }

    internal NfcCorroboration(bool available, bool communicationSucceeded, bool? passiveAuthenticationPassed)
    {
        Available = available;
        CommunicationSucceeded = communicationSucceeded;
        PassiveAuthenticationPassed = passiveAuthenticationPassed;
    }

    /// <summary>No NFC read was attempted or supplied for this comparison at all.</summary>
    public static readonly NfcCorroboration NotAvailable = new(available: false, communicationSucceeded: false, passiveAuthenticationPassed: null);

    public static NfcCorroboration From(ChipReadOutcome outcome) => new(
        available: true,
        communicationSucceeded: outcome.ChipCommunicationSucceeded,
        passiveAuthenticationPassed: outcome.PassiveAuthentication.Attempted ? outcome.PassiveAuthentication.Passed : null);
}
