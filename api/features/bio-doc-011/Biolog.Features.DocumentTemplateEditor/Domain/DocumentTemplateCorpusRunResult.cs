namespace Biolog.Features.DocumentTemplateEditor.Domain;

/// <summary>
/// AC03: the result of actually running a test corpus against the draft's declared field
/// regions through BIO-DOC-004's real <c>TemplateOcrEngine</c> (<see cref="SyntheticCorpusRenderer"/>).
/// <see cref="Passed"/> is computed by <see cref="DocumentTemplateDraftService.RunCorpusAsync"/> -
/// never a caller-supplied boolean the HTTP surface could forge (AC03's own "must pass" is an
/// enforced gate, not an attestation; contrast BIO-CRD-008's own <c>RecordValidationAsync(bool
/// passed, ...)</c>, which deliberately trusts its own caller for a different, human-attested
/// validation step).
/// </summary>
public sealed record DocumentTemplateCorpusRunResult
{
    public bool Passed { get; internal init; }
    public int SampleCount { get; internal init; }
    public IReadOnlyList<string> FailureReasons { get; internal init; } = [];
    public string RanAt { get; internal init; } = null!;

    internal DocumentTemplateCorpusRunResult(bool passed, int sampleCount, IReadOnlyList<string> failureReasons, string ranAt)
    {
        Passed = passed;
        SampleCount = sampleCount;
        FailureReasons = failureReasons;
        RanAt = ranAt;
    }
}
