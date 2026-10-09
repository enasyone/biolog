using System.Security.Cryptography.X509Certificates;

namespace Biolog.Features.NfcReader.Model;

/// <summary>The already-extracted evidence <see cref="PassiveAuthentication.Evaluate"/> needs
/// (BIO-DOC-007-AC04) - see <see cref="PassiveAuthentication"/>'s remarks on why this story does
/// not parse a raw EF.SOD itself. Optional on <see cref="ChipReadSession.Read"/>: when a caller
/// does not have this material yet, passive authentication is explicitly not attempted rather than
/// silently skipped without a trace.</summary>
public sealed record PassiveAuthenticationInputs(
    IReadOnlyDictionary<ChipDataGroupId, string> SodDeclaredHashes,
    X509Certificate2 DocumentSignerCertificate,
    X509Certificate2Collection TrustedCscaCertificates);
