namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>
/// BIO-DOC-009-AC03: "Unsupported checks are marked not performed, not passed." <see cref="NotPerformed"/>
/// is deliberately the FIRST member so <c>default(AuthenticityCheckOutcome)</c> is the safe value -
/// a caller who forgets to set an outcome explicitly gets "not performed", never an accidental
/// "passed" (the same fail-safe-toward-the-least-trusting-state discipline
/// <c>web/lib/feature-mode.ts</c>'s <c>getPortMode</c> already applies on the frontend side of this
/// same story).
/// </summary>
public enum AuthenticityCheckOutcome
{
    NotPerformed,
    Passed,
    Failed,
    Inconclusive,
}
