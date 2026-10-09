using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Biolog.Features.EvidenceComparison.Tests.Fixtures;

/// <summary>Own local copy of BIO-DOC-007's identically named fixture (builder constraint: "use
/// synthetic data only") - a self-signed offline root ("CSCA") and a certificate it signs
/// ("DSC"), using <see cref="CertificateRequest"/>, part of the .NET base class library.</summary>
public static class SyntheticCertificates
{
    public static (X509Certificate2 Csca, X509Certificate2 Dsc) BuildTrustedChain(string label = "Test")
    {
        using var cscaKey = RSA.Create(2048);
        var cscaRequest = new CertificateRequest($"CN={label} CSCA", cscaKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        cscaRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        var csca = cscaRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));

        using var dscKey = RSA.Create(2048);
        var dscRequest = new CertificateRequest($"CN={label} DSC", dscKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        dscRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        var dsc = dscRequest.Create(csca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), serial);

        return (csca, dsc);
    }
}
