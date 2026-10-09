using System.Security.Cryptography.X509Certificates;

namespace Biolog.Features.NfcReader.Model;

/// <summary>
/// Evaluates passive authentication (BIO-DOC-007-AC04) against already-extracted evidence: each
/// read data group's hash compared to the Document Security Object's declared hash for it,
/// EVERY SOD-declared data group checked for actually having been read at all (a group the SOD
/// declares but that was simply never read is missing evidence, not a match - BIO-DOC-007-QA-01),
/// and the Document Signer Certificate's chain validated against a trusted set of Country Signing
/// CA certificates using .NET's real <see cref="X509Chain"/> (part of
/// <c>System.Security.Cryptography.X509Certificates</c> - base class library, no NuGet package).
///
/// <para>
/// <b>Scope: this does not parse a raw EF.SOD.</b> A real ICAO 9303 SOD is a CMS/PKCS7
/// <c>SignedData</c> structure wrapping an ASN.1 <c>LDSSecurityObject</c> (the per-data-group hash
/// table) and embedding the Document Signer Certificate. Parsing that raw byte structure end to
/// end - CMS envelope plus the LDS-specific ASN.1 content inside it - is a document-forensics-
/// grade parser out of proportion for this story; <c>System.Security.Cryptography.Pkcs.SignedCms</c>
/// could parse the CMS envelope, but nothing in the base class library parses
/// <c>LDSSecurityObject</c>'s own ASN.1 shape. This method instead takes the already-extracted
/// <paramref name="sodDeclaredHashes"/> and <paramref name="documentSignerCertificate"/> as input -
/// the same "the caller already ran X, hand us the result" shape BIO-DOC-006's classifier used for
/// already-run OCR template matches. A later story that adds real SOD parsing feeds this method's
/// inputs from that parser's output without this method changing.
/// </para>
/// </summary>
public static class PassiveAuthentication
{
    public static PassiveAuthenticationResult Evaluate(
        IReadOnlyList<ChipDataGroupRead> readDataGroups,
        IReadOnlyDictionary<ChipDataGroupId, string> sodDeclaredHashes,
        X509Certificate2 documentSignerCertificate,
        X509Certificate2Collection trustedCscaCertificates)
    {
        var mismatched = new List<ChipDataGroupId>();
        var readIds = new HashSet<ChipDataGroupId>();
        foreach (var dataGroup in readDataGroups)
        {
            readIds.Add(dataGroup.Id);
            if (!sodDeclaredHashes.TryGetValue(dataGroup.Id, out var declaredHash)
                || !string.Equals(declaredHash, dataGroup.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                mismatched.Add(dataGroup.Id);
            }
        }

        // A data group the SOD declared a hash for, but that was never actually read at all, is
        // not evidence of a match - it is missing evidence, and must not be silently treated as
        // consistent with what WAS read (BIO-DOC-007-QA-01).
        var missing = sodDeclaredHashes.Keys.Where(id => !readIds.Contains(id)).ToList();

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Clear();
        chain.ChainPolicy.CustomTrustStore.AddRange(trustedCscaCertificates);
        // No CRL/OCSP infrastructure exists in this environment (synthetic, offline test
        // certificates have no real revocation endpoint) - revocation checking is out of this
        // story's scope, documented explicitly rather than silently disabled.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        var chainValid = chain.Build(documentSignerCertificate);
        var statusDescriptions = chain.ChainStatus.Select(s => s.Status.ToString()).ToList();

        return new PassiveAuthenticationResult(
            attempted: true,
            dataGroupHashesMatch: mismatched.Count == 0 && missing.Count == 0,
            mismatchedDataGroups: mismatched,
            missingDataGroups: missing,
            certificateChainValid: chainValid,
            certificateChainStatus: statusDescriptions);
    }
}
