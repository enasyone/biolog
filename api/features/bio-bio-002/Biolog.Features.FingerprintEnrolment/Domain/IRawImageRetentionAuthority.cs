namespace Biolog.Features.FingerprintEnrolment.Domain;

/// <summary>
/// BIO-BIO-009-AC03 (Seyi round-2 finding BIO-BIO-009-F01 / Zainab ZB-BIO009-03): the
/// AUTHORITATIVE, tenant-scoped decision of whether a raw fingerprint image may be retained at
/// all, server-side. Before this interface existed, <see cref="Service.FingerprintEnrolmentService.CaptureFingerAsync"/>
/// retained a raw image purely from <see cref="FingerprintEnrolmentSession.RetentionAuthorised"/>,
/// a caller-controlled flag asserted at session start - a tenant that was never authorised (or was
/// explicitly revoked) in BIO-BIO-009's own policy could still have raw images retained by a
/// caller simply passing <c>true</c>. This story (BIO-BIO-002) cannot reference the later
/// BIO-BIO-009 story directly (a feature project never depends on a later one), so it owns this
/// seam itself - the same dependency-inversion direction as BIO-BIO-009's own
/// <c>ITemplateDeletionGateway</c>, just pointed the other way. The concrete adapter wrapping
/// BIO-BIO-009's real <c>IRawImageRetentionPolicy</c> is registered in <c>Biolog.Api</c>
/// (<c>BioBio009Module</c>), the one place with project references to both.
/// </summary>
internal interface IRawImageRetentionAuthority
{
    /// <summary>Must fail closed: any inability to reach the authoritative policy is a caller
    /// bug to surface as "not authorised", never "assume authorised".</summary>
    Task<bool> IsAuthorisedAsync(string tenantId, CancellationToken cancellationToken);
}
