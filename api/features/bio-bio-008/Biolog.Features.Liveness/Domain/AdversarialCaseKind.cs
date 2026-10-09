namespace Biolog.Features.Liveness.Domain;

/// <summary>
/// BIO-BIO-008-AC04: "Replay/screen/photo adversarial cases are represented in test interfaces."
/// No real anti-spoofing model exists anywhere in this repo (see <see cref="SyntheticLivenessProvider"/>'s
/// own remarks) so this is not a detection taxonomy - it is a named, selectable set of scenarios
/// <see cref="AdversarialSampleFactory"/> can produce on demand, the same "hint" pattern
/// BIO-UX-008's own mock adapters use for their outcome selectors.
/// </summary>
internal enum AdversarialCaseKind
{
    Replay,
    ScreenPresentation,
    PhotoPresentation,
}
