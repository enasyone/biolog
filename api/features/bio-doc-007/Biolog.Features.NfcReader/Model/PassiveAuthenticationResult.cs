namespace Biolog.Features.NfcReader.Model;

/// <summary>
/// The explicit result of passive authentication (BIO-DOC-007-AC04: "passive authentication/
/// certificate-chain results are explicit where supported"). <see cref="Attempted"/> is false
/// whenever passive authentication was never run at all (e.g. the Document Security Object was
/// never read) - a caller must never read <see cref="Passed"/> as "false" meaning "failed" when it
/// actually means "not even checked"; <see cref="Passed"/> is only meaningful once
/// <see cref="Attempted"/> is true.
///
/// <para>
/// <see cref="DataGroupHashesMatch"/> and <see cref="CertificateChainValid"/> are two
/// INDEPENDENTLY reported outcomes, not folded into one - a caller that needs to know specifically
/// "did the chip's OWN data get tampered with" versus "is the signer even trustworthy" can tell
/// them apart, and <see cref="MismatchedDataGroups"/> names exactly which data groups failed
/// rather than a bare boolean.
/// </para>
///
/// <para>
/// <see cref="DataGroupHashesMatch"/> is true only when EVERY data group the SOD declared a hash
/// for was both actually read AND matches - <see cref="MissingDataGroups"/> names any SOD-declared
/// group that was never read at all, distinct from <see cref="MismatchedDataGroups"/> (read, but
/// the hash disagrees, or read but the SOD never declared it). A chip that simply omits a declared
/// group from what it returns is not evidence of a match on the groups it DID hand over - it is
/// incomplete evidence, and incomplete evidence must not report as passing (BIO-DOC-007-QA-01,
/// Seyi's independent review).
/// </para>
///
/// <para>Locked down (<c>internal init</c>, no public constructor) - the same reasoning as every
/// prior BIO-DOC-00x trust-bearing result type: a public <c>init</c> would let a caller launder a
/// failed passive-authentication result into one that claims <see cref="Passed"/>.</para>
///
/// <para>
/// <b>BIO-DOC-007-QA-03 (Chioma's second acceptance review):</b> every
/// <c>IReadOnlyList&lt;T&gt;</c> property here is now a fresh copy wrapped in
/// <c>List&lt;T&gt;.AsReadOnly()</c>, not the caller's own list viewed through the interface -
/// see <see cref="ChipReadOutcome"/>'s remarks for why a plain interface-typed property over a
/// caller-supplied <c>List&lt;T&gt;</c> was still downcastable and mutable at runtime.
/// </para>
/// </summary>
public sealed record PassiveAuthenticationResult
{
    public bool Attempted { get; internal init; }
    public bool DataGroupHashesMatch { get; internal init; }
    public IReadOnlyList<ChipDataGroupId> MismatchedDataGroups { get; internal init; } = [];
    public IReadOnlyList<ChipDataGroupId> MissingDataGroups { get; internal init; } = [];
    public bool CertificateChainValid { get; internal init; }
    public IReadOnlyList<string> CertificateChainStatus { get; internal init; } = [];

    /// <summary>Only meaningful when <see cref="Attempted"/> is true.</summary>
    public bool Passed => Attempted && DataGroupHashesMatch && CertificateChainValid;

    internal PassiveAuthenticationResult(
        bool attempted,
        bool dataGroupHashesMatch,
        IReadOnlyList<ChipDataGroupId> mismatchedDataGroups,
        IReadOnlyList<ChipDataGroupId> missingDataGroups,
        bool certificateChainValid,
        IReadOnlyList<string> certificateChainStatus)
    {
        Attempted = attempted;
        DataGroupHashesMatch = dataGroupHashesMatch;
        // Fresh copies wrapped in AsReadOnly() - not the caller's own mutable List<T> viewed
        // through the interface - so none of these can be downcast back to a mutable list and
        // altered after the fact (BIO-DOC-007-QA-03, the same lesson QA-02 taught for byte[] key
        // material, applied here to collections).
        MismatchedDataGroups = new List<ChipDataGroupId>(mismatchedDataGroups).AsReadOnly();
        MissingDataGroups = new List<ChipDataGroupId>(missingDataGroups).AsReadOnly();
        CertificateChainValid = certificateChainValid;
        CertificateChainStatus = new List<string>(certificateChainStatus).AsReadOnly();
    }

    internal static readonly PassiveAuthenticationResult NotAttempted = new(
        attempted: false,
        dataGroupHashesMatch: false,
        mismatchedDataGroups: [],
        missingDataGroups: [],
        certificateChainValid: false,
        certificateChainStatus: []);
}
