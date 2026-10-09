using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.DocumentTemplatePack.Audit;
using Biolog.Features.DocumentTemplatePack.Data;
using Biolog.Features.DocumentTemplatePack.Model;
using Biolog.Features.DocumentTemplatePack.Store;

namespace Biolog.Features.DocumentTemplatePack.Api;

/// <summary>
/// Application-layer orchestration for the BIO-DOC-010 HTTP surface (AC05): publishes
/// <see cref="NigeriaDocumentPackV1"/> once at construction (registering its governed identities
/// into this instance's own <see cref="DocumentIntelligenceRegistry"/> - BIO-DOC-006), registers
/// its governed samples (AC03), and cycles deterministically through three named demo scenarios
/// (mirroring <c>AuthenticityAssessmentService</c>'s own demo-cycle shape) so
/// <c>POST /api/v1/documents/identify</c> returns a genuinely computed - not canned -
/// <see cref="DocumentTemplateVerificationOutcome"/> every time, covering all three
/// <see cref="DocumentTemplateVerificationState"/> values without needing a real capture/upload
/// pipeline (none exists yet - the same disclosed boundary BIO-DOC-009's own demo fixtures
/// already document).
///
/// <para>
/// <b>No static/shared state, deliberately:</b> every field below is instance-owned, the same
/// "no hidden shared mutable state" discipline <c>SyntheticAuthenticityFixtures</c>'s own fix
/// (BIO-DOC-009-F01's own follow-on bug) established for this epic - a second instance of this
/// service (e.g. one per test) starts its own independent pack/registry/audit trail, never
/// desynchronising with another instance's.
/// </para>
/// </summary>
public sealed class DocumentTemplatePackService
{
    private readonly object _gate = new();
    private readonly DocumentIntelligenceRegistry _registry = new();
    private readonly IDocumentTemplatePackStore _packStore;
    private readonly IDocumentTemplateSampleStore _sampleStore;
    private readonly IDocumentTemplatePackAuditSink _auditSink;
    private readonly Model.DocumentTemplatePack _currentPack;
    private readonly IReadOnlyList<(string EntryId, IReadOnlyDictionary<string, string> ObservedFields)> _demoScenarios = NigeriaDocumentPackV1.DemoScenarios();
    private int _demoCallCount;

    public DocumentTemplatePackService(IDocumentTemplatePackStore packStore, IDocumentTemplateSampleStore sampleStore, IDocumentTemplatePackAuditSink auditSink)
    {
        _packStore = packStore;
        _sampleStore = sampleStore;
        _auditSink = auditSink;

        _currentPack = NigeriaDocumentPackV1.Build();
        _currentPack.Publish("system:document-template-pack-seed", DocumentTemplatePackClock.Now(), DocumentTemplatePackEventId.New(), _registry, _auditSink);
        _packStore.Save(_currentPack);

        foreach (var sample in NigeriaDocumentPackV1.Samples())
        {
            _sampleStore.Add(_currentPack, sample);
        }
    }

    /// <summary>Read-only access to the registry this service's pack was published into - exposed
    /// for tests proving BIO-DOC-006 integration (AC01's "sides"/AC02's registration), and for a
    /// later story that needs to classify against these governed identities.</summary>
    public DocumentIntelligenceRegistry Registry => _registry;

    public Model.DocumentTemplatePack CurrentPack => _currentPack;

    public IReadOnlyList<DocumentTemplateSample> SamplesFor(string entryId) => _sampleStore.GetForEntry(entryId);

    /// <summary>Computes the next deterministic demo verification and returns the resulting
    /// outcome - never returns without a durably recorded pack version (AC04).</summary>
    public DocumentTemplateVerificationOutcome IdentifyNextDemo(string actorId, string correlationId)
    {
        lock (_gate)
        {
            var index = _demoCallCount % _demoScenarios.Count;
            _demoCallCount += 1;
            var (entryId, observedFields) = _demoScenarios[index];

            return DocumentTemplateVerification.Verify(
                _currentPack, entryId, observedFields, actorId, correlationId, DocumentTemplatePackClock.Now(), _auditSink);
        }
    }

    /// <summary>
    /// BIO-OPS-005 (Kemi's ruling, point 3): the real-evidence counterpart to
    /// <see cref="IdentifyNextDemo"/> - added ALONGSIDE it, never replacing it. Verifies a
    /// genuinely observed field set (assembled by BIO-OPS-005's own coordinator from real
    /// MRZ/OCR evidence) against a caller-named entry in this instance's own governed pack,
    /// instead of cycling a fixed demo scenario.
    /// </summary>
    public DocumentTemplateVerificationOutcome IdentifyFromEvidence(
        string entryId, IReadOnlyDictionary<string, string> observedFields, string actorId, string correlationId)
    {
        lock (_gate)
        {
            return DocumentTemplateVerification.Verify(
                _currentPack, entryId, observedFields, actorId, correlationId, DocumentTemplatePackClock.Now(), _auditSink);
        }
    }
}
