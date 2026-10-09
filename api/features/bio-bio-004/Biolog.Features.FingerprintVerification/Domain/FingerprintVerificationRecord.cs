namespace Biolog.Features.FingerprintVerification.Domain;

/// <summary>
/// AC02: "Provider score/decision/model version are recorded." One immutable, append-only record
/// of a single 1:1 verification attempt against a claimed identity - never edited or superseded
/// (unlike an enrolment aggregate, a past verification attempt is a historical fact, not
/// something a later call corrects). <see cref="Service.FingerprintVerificationService.VerifyAsync"/>
/// writes exactly one of these per call, regardless of which <see cref="BiometricComparisonOutcome"/>
/// or <see cref="AccessDecision"/> it produced - AC02 does not say "only successful attempts are
/// recorded."
///
/// <see cref="ProviderId"/>/<see cref="ProviderVersion"/> name the CLAIMED IDENTITY'S authorised
/// reference's own provider/model version (AC01's resolved reference, not necessarily the live
/// probe's provider version, though in this synthetic host they are always the same provider) -
/// the "model version" AC02 asks be recorded is about what the reference was enrolled with, the
/// fact a security review needs to know if a provider is later deprecated.
///
/// <see cref="Score"/>/<see cref="MatcherId"/>/<see cref="MatchedTemplateId"/> are null exactly
/// when <see cref="ComparisonOutcome"/> is <see cref="BiometricComparisonOutcome.AcquisitionFailed"/>
/// - no comparison ever ran. <see cref="DenyReason"/> is null exactly when
/// <see cref="AccessDecision"/> is <see cref="Domain.AccessDecision.Allow"/>.
/// </summary>
public sealed record FingerprintVerificationRecord
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required BiometricComparisonOutcome ComparisonOutcome { get; init; }
    public required AccessDecision AccessDecision { get; init; }
    public string? DenyReason { get; init; }
    public double? Score { get; init; }
    public string? MatcherId { get; init; }
    public string? MatchedTemplateId { get; init; }
    public required string ProviderId { get; init; }
    public required string ProviderVersion { get; init; }
    public required string OccurredAtUtc { get; init; }
}
