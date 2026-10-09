using Biolog.Features.DocumentClassifier.Model;

namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>
/// AC03's "reference/test samples are governed and synthetic where required": one named,
/// synthetic field-value set for one governed <see cref="DocumentTemplatePackEntry"/>/<see cref="Side"/>.
///
/// <para>
/// <b>"Synthetic" is structural, not a caller-supplied flag:</b> there is no boolean parameter a
/// caller could set to <c>false</c> - every <see cref="DocumentTemplateSample"/> that exists is
/// synthetic by construction, matching this repository's standing "use synthetic data only"
/// constraint the same way <see cref="AuthenticityDisposition.DocumentAuthentic"/> can never be
/// reached from barcode decode alone (BIO-DOC-009-AC02): the guarantee is that no code path can
/// produce a non-synthetic instance, not that one happens not to today.
/// </para>
///
/// <para>
/// <b>"Governed":</b> a sample is only ever accepted by
/// <see cref="Store.IDocumentTemplateSampleStore.Add"/> once that store has confirmed
/// <see cref="EntryId"/> names a real entry in the given pack - see
/// <see cref="Store.UngovernedSampleEntryException"/>.
/// </para>
/// </summary>
public sealed record DocumentTemplateSample
{
    public string SampleId { get; }
    public string EntryId { get; }
    public DocumentSide Side { get; }
    public string Label { get; }
    public bool IsSynthetic => true;
    public IReadOnlyDictionary<string, string> FieldValues { get; }

    public DocumentTemplateSample(string sampleId, string entryId, DocumentSide side, string label, IReadOnlyDictionary<string, string> fieldValues)
    {
        if (string.IsNullOrWhiteSpace(sampleId))
        {
            throw new ArgumentException("sampleId must not be blank", nameof(sampleId));
        }

        if (string.IsNullOrWhiteSpace(entryId))
        {
            throw new ArgumentException("entryId must not be blank", nameof(entryId));
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("label must not be blank", nameof(label));
        }

        ArgumentNullException.ThrowIfNull(fieldValues);

        SampleId = sampleId;
        EntryId = entryId;
        Side = side;
        Label = label;
        FieldValues = new Dictionary<string, string>(fieldValues);
    }
}
