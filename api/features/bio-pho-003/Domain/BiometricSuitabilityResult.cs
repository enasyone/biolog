namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// BIO-PHO-003-AC01's biometric-suitability side, deliberately independent of
/// <see cref="PrintQualityGate.Verdict"/> - either can pass while the other fails. This
/// feature's own domain layer never references BIO-BIO-003's types directly (no
/// <c>ProjectReference</c> is allowed) - <see cref="IBiometricSuitabilityGateway"/> is the
/// narrow seam that returns this plain, feature-local shape instead; the real implementation
/// (an adapter in <c>Biolog.Api.Modules.BioPho003Module</c>) is the only place that ever
/// touches BIO-BIO-003's own <c>IFaceQualityChecker</c>/<c>ILivenessDetector</c>.
/// </summary>
public sealed record BiometricSuitabilityResult(bool Passed, IReadOnlyList<string> Reasons);
