namespace Biolog.Features.DocumentTemplateEditor.Domain;

/// <summary>
/// AC03's "test corpus": one sample a tenant supplies to prove the drafted field regions
/// actually extract correctly before publish is allowed. <see cref="ExpectedFieldValues"/> names,
/// for every field region the draft declares, the value a correct read should produce - this
/// project's synthetic renderer (<see cref="SyntheticCorpusRenderer"/>) stamps exactly these
/// characters into a <c>DocumentRaster</c> at each field's own declared region, so a correctly
/// defined template always passes its own honestly-described corpus. <see cref="CorruptFieldNames"/>
/// (empty for an ordinary happy-path sample) names fields the renderer deliberately stamps with a
/// DIFFERENT value than <see cref="ExpectedFieldValues"/> - the mechanism this story's own tests
/// use to prove AC03's gate genuinely fails a bad sample, not only ever passes one.
/// </summary>
public sealed record DocumentTemplateCorpusSample(
    string SampleId,
    IReadOnlyDictionary<string, string> ExpectedFieldValues,
    IReadOnlySet<string>? CorruptFieldNames = null);
