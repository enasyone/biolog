using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.DocumentVerify.Model;
using Biolog.Features.DocumentVerify.Storage;
using Biolog.Features.DocumentVerifyCoordination.Api;
using Biolog.Features.DocumentVerifyCoordination.Fixtures;
using Biolog.Features.DocumentVerifyCoordination.Gateways;
using Biolog.Features.DocumentVerifyCoordination.Tenancy;
using Biolog.Features.EvidenceComparison.Audit;
using Biolog.Features.EvidenceComparison.Model;
using MrzModel = Biolog.Features.MrzEngine.Model;
using OcrModel = Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.DocumentVerifyCoordination;

/// <summary>
/// BIO-DOC-008's own version-sequencing protocol requires the CALLER to track and present its own
/// next expected version per document session (see <c>CrossComparisonResult.Compute</c>'s own
/// remarks and its test suite's identical caller-side tracking); that tracking, plus the shared
/// audit sink/version store every <see cref="DocumentVerifyCoordinatorService"/> call must reuse
/// (never a fresh instance per call - a fresh <c>InMemoryVersionStore</c> every call would see no
/// committed history at all and defeat the whole protocol), is registered a SINGLETON (the same
/// "one instance, useful across requests" reasoning <c>DocumentModule</c> already documents for
/// <c>InMemoryDocumentSessionStore</c>) and injected into the per-request, SCOPED
/// <see cref="DocumentVerifyCoordinatorService"/> - a scoped service cannot itself hold this
/// state directly without becoming a captive-dependency hazard if it were ever made a singleton
/// alongside its own scoped <see cref="Tenancy.ICurrentTenantProvider"/> dependency.
/// </summary>
public sealed class DocumentComparisonVersioningState
{
    public IEvidenceComparisonAuditSink AuditSink { get; } = new InMemoryEvidenceComparisonAuditSink();
    public IVersionStore VersionStore { get; } = new InMemoryVersionStore();
    private readonly ConcurrentDictionary<string, int> _nextVersionBySession = new(StringComparer.Ordinal);

    public int NextVersionNumber(string documentSessionId) =>
        _nextVersionBySession.AddOrUpdate(documentSessionId, 1, (_, current) => current + 1);
}

/// <summary>
/// The Document Verify coordinator Kemi's BIO-OPS-005 ruling calls for: one session, real
/// engines, real evidence, real cross-source comparison and real identification - composition
/// only, no new domain model (BIO-DOC-001's own session/evidence model already has every
/// operation this needs). Resolves the tenant itself (point 4/Tenancy seam below), never trusts a
/// caller-supplied one.
///
/// <para>
/// <b>Raster boundary (point 6):</b> every "read" below runs the REAL BIO-DOC-002/003/004 engine
/// code against a deterministic synthetic raster (<see cref="SyntheticDocumentFixture"/>) - only
/// the input image is synthetic, never the engine's own recognition/validation logic or this
/// coordinator's own evidence-recording/comparison/review logic.
/// </para>
///
/// <para>
/// <b>Pre-person sessions and PERSON proposals (BIO-DOC-001-AC04):</b> <see cref="CaptureAsync"/>
/// opens a session with no person linked (the "kiosk capturing a document before the visitor's
/// Person record is created" path BIO-DOC-001's own <c>DocumentSession</c> remarks describe).
/// <see cref="IDocumentSessionStore.ProposePersonFieldChange"/> requires a linked person; this
/// coordinator calls it only when <see cref="DocumentSession.PersonId"/> is already set - which
/// some OTHER, later onboarding step (not any of this story's own 8 operations) is responsible
/// for linking. This is not a gap: a session that genuinely has no person yet correctly has
/// nothing to propose a PERSON-field change for yet, the same pre-person/person-linked duality
/// BIO-DOC-001's own domain model already names.
/// </para>
/// </summary>
public sealed class DocumentVerifyCoordinatorService(
    ICurrentTenantProvider tenantProvider,
    IDocumentSessionStore sessionStore,
    IAuthenticityAssessmentGateway authenticityGateway,
    IDocumentIdentificationGateway identificationGateway,
    DocumentComparisonVersioningState comparisonState,
    DocumentVerifyLockRegistry lockRegistry)
{
    /// <summary>The one canonical field both MRZ (<c>document_number</c>) and OCR
    /// (<c>PassportNumber</c>) genuinely produce a comparable VALUE for (not just a hash, unlike
    /// barcode - see <see cref="RunBarcodeAsync"/>'s own remarks) - aliased to this one shared key
    /// so <see cref="CheckAuthenticityAsync"/> can cross-compare them as the same field.</summary>
    private const string ComparableDocumentNumberField = "document_number";

    /// <summary>The one governed pack entry <see cref="SyntheticDocumentFixture"/> always
    /// targets - <c>NigeriaDocumentPackV1.PassportEntryId</c>'s own literal value (no
    /// <c>ProjectReference</c> to BIO-DOC-010 to see that constant with).</summary>
    private const string FixtureEntryId = "nga-passport-2021";

    public async Task<(string SessionId, EvidenceStepOutcome Outcome)> CaptureAsync(
        string actorId, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var sessionId = CanonicalId.NewDocumentSessionId();
        var now = UtcInstant.Now();

        sessionStore.CreateSession(sessionId, tenantId, personId: null, now);

        var captureHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"synthetic-fixture:{sessionId}")));
        var evidence = sessionStore.AppendEvidence(
            tenantId, sessionId, captureHash, mimeType: "application/x-biolog-synthetic-raster",
            pageNumber: 1, side: DocumentSide.Front, source: EvidenceSource.Capture,
            capturedAt: now, receivedAt: now, classification: EvidenceClassification.Personal);

        var outcome = new EvidenceStepOutcome(
            "success", "read", "Document captured.", 1.0,
            new EvidenceProvenance("synthetic-fixture", evidence.SequenceNumber, null));

        return (sessionId, outcome);
    }

    public async Task<EvidenceStepOutcome> RunMrzAsync(string sessionId, string actorId, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var captureEvidence = RequireCaptureEvidence(tenantId, sessionId);
        var now = UtcInstant.Now();

        var raster = SyntheticDocumentFixture.BuildMrzRaster();
        var read = MrzModel.MrzReader.Read(raster);

        if (!read.Detected)
        {
            return new EvidenceStepOutcome(
                "failure", "failed", "MRZ was not detected on the captured document.", 0.0,
                new EvidenceProvenance("mrz", captureEvidence.SequenceNumber, []));
        }

        var fieldSequenceNumbers = new List<int>();
        var minConfidence = 1.0;
        foreach (var field in read.Fields)
        {
            // MrzParser's own BaseName switch already names this field "document_number" - the
            // exact ComparableDocumentNumberField key RunOcrAsync aliases its own "PassportNumber"
            // field onto, so no rename is needed here (unlike OCR's own case below).
            var fe = sessionStore.AppendFieldEvidence(
                tenantId, sessionId, captureEvidence.SequenceNumber, field.FieldName,
                field.RawValue, field.NormalizedValue, field.Confidence,
                FieldEvidenceSource.Mrz, field.BoundingBox, now, EvidenceClassification.Personal);
            fieldSequenceNumbers.Add(fe.SequenceNumber);
            minConfidence = Math.Min(minConfidence, field.Confidence);

            await TryProposePersonFieldChangeAsync(tenantId, sessionId, MrzPersonField(field.Kind), fe, now, cancellationToken);
        }

        var state = read.RequiresManualReview ? "inconclusive" : "structurally-valid";
        var message = read.RequiresManualReview
            ? $"MRZ read, but requires review: {string.Join(", ", read.ReviewReasons)}."
            : "MRZ read and every check digit (including the composite) is valid.";

        return new EvidenceStepOutcome("success", state, message, minConfidence,
            new EvidenceProvenance("mrz", captureEvidence.SequenceNumber, fieldSequenceNumbers));
    }

    /// <summary>
    /// BIO-DOC-003's own <see cref="BarcodeDecodeResult"/> deliberately withholds the decoded
    /// payload bytes (only a SHA-256 hash - see that type's own remarks: a decoded barcode can
    /// carry identity PII in plain text), so unlike MRZ/OCR this evidence is NOT fed into
    /// <see cref="CheckAuthenticityAsync"/>'s cross-source comparison - there is no comparable
    /// VALUE this layer can honestly recover, only a tamper-evident fingerprint. This matches
    /// BIO-DOC-008's own declared dependency set (BIO-DOC-002/BIO-DOC-005, deliberately not
    /// BIO-DOC-003) and BIO-DOC-003-AC04's own rule ("no decoded barcode alone is labelled
    /// authentic").
    /// </summary>
    public async Task<EvidenceStepOutcome> RunBarcodeAsync(string sessionId, string actorId, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var captureEvidence = RequireCaptureEvidence(tenantId, sessionId);
        var now = UtcInstant.Now();

        var raster = SyntheticDocumentFixture.BuildBarcodeRaster();
        var service = new BarcodeDetectionService(BarcodeDecoderRegistry.CreateDefault());
        var scan = service.Scan(raster);

        if (scan.Decoded.Count == 0)
        {
            return new EvidenceStepOutcome(
                "failure", "failed", "No barcode could be decoded on the captured document.", 0.0,
                new EvidenceProvenance("barcode", captureEvidence.SequenceNumber, []));
        }

        var decoded = scan.Decoded[0];
        var fe = sessionStore.AppendFieldEvidence(
            tenantId, sessionId, captureEvidence.SequenceNumber, "barcode_payload",
            decoded.RawPayloadHash, decoded.RawPayloadHash, decoded.Confidence,
            FieldEvidenceSource.Barcode, decoded.BoundingBox, now,
            EvidenceClassification.Personal);

        return new EvidenceStepOutcome(
            "success", "structurally-valid", $"Barcode decoded ({decoded.Symbology}) - payload hash recorded as evidence.", decoded.Confidence,
            new EvidenceProvenance("barcode", captureEvidence.SequenceNumber, [fe.SequenceNumber]));
    }

    public async Task<EvidenceStepOutcome> RunOcrAsync(string sessionId, string actorId, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var captureEvidence = RequireCaptureEvidence(tenantId, sessionId);
        var now = UtcInstant.Now();

        var raster = SyntheticDocumentFixture.BuildOcrRaster();
        var template = SyntheticDocumentFixture.BuildOcrTemplate();
        var result = OcrModel.TemplateOcrEngine.Read(raster, template, new OcrModel.EnglishLanguagePack());

        var fieldSequenceNumbers = new List<int>();
        var minConfidence = 1.0;
        foreach (var field in result.Fields)
        {
            var comparableFieldName = field.FieldName == "PassportNumber" ? ComparableDocumentNumberField : field.FieldName;
            var fe = sessionStore.AppendFieldEvidence(
                tenantId, sessionId, captureEvidence.SequenceNumber, comparableFieldName,
                field.RawValue, field.NormalizedValue, field.Confidence,
                FieldEvidenceSource.Ocr, field.BoundingBox, now, EvidenceClassification.Personal);
            fieldSequenceNumbers.Add(fe.SequenceNumber);
            minConfidence = Math.Min(minConfidence, field.Confidence);

            await TryProposePersonFieldChangeAsync(tenantId, sessionId, OcrPersonField(field.FieldName), fe, now, cancellationToken);
        }

        var state = result.RequiresManualReview ? "inconclusive" : "read";
        var message = result.RequiresManualReview
            ? $"OCR extracted every field, but requires review: {string.Join(", ", result.ReviewReasons)}."
            : "OCR extracted every templated field above the confidence threshold.";

        return new EvidenceStepOutcome("success", state, message, minConfidence,
            new EvidenceProvenance("ocr", captureEvidence.SequenceNumber, fieldSequenceNumbers));
    }

    /// <summary>
    /// Kemi's ruling, point 5: never fabricate a chip read. <see cref="Biolog.Features.NfcReader.Model.IChipDataGroupTransport"/>
    /// has no implementation anywhere in this codebase (physical NFC hardware is required - see
    /// that interface's own remarks), so this coordinator never constructs one and never calls
    /// <see cref="Biolog.Features.NfcReader.Model.ChipReadSession.Read"/>. No field evidence is
    /// appended either - there is nothing genuinely read to record.
    /// </summary>
    public async Task<EvidenceStepOutcome> RunNfcAsync(string sessionId, string actorId, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        RequireCaptureEvidence(tenantId, sessionId);

        return new EvidenceStepOutcome(
            "success", "not-checked",
            "NFC passive authentication was not performed - no physical chip transport is available in this environment.",
            0.0, new EvidenceProvenance("unavailable", null, null));
    }

    public async Task<EvidenceStepOutcome> IdentifyDocumentAsync(string sessionId, string actorId, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        RequireCaptureEvidence(tenantId, sessionId);

        var observedFields = BuildObservedFieldsFromOcrEvidence(tenantId, sessionId);
        var snapshot = await identificationGateway.IdentifyFromEvidenceAsync(sessionId, FixtureEntryId, observedFields, actorId, correlationId, cancellationToken);

        return new EvidenceStepOutcome("success", snapshot.EvidenceCheckState, snapshot.Summary, snapshot.Confidence,
            new EvidenceProvenance("ocr", null, null));
    }

    /// <summary>
    /// Kemi's ruling, point 5: runs <see cref="ComparisonEngine.Compare"/> per field across
    /// whichever sources this session's accumulated field evidence actually carries, raising a
    /// genuine <see cref="DocumentSession"/> conflict (point 5's "session -&gt;
    /// ManualReviewPending, UI state='inconclusive'") for every field BIO-DOC-008 found a real
    /// mismatch on, then calls the new <c>ComputeFromEvidence</c> overload fed by those same
    /// records.
    /// </summary>
    public async Task<EvidenceStepOutcome> CheckAuthenticityAsync(string sessionId, string actorId, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        RequireCaptureEvidence(tenantId, sessionId);
        var now = UtcInstant.Now();

        using var _ = await lockRegistry.AcquireAsync(sessionId, cancellationToken);

        var fieldEvidence = sessionStore.ListFieldEvidence(tenantId, sessionId);
        var byField = fieldEvidence.GroupBy(f => f.FieldName, StringComparer.Ordinal);

        var fieldComparisons = new List<FieldComparison>();
        foreach (var group in byField)
        {
            var valuesBySource = new Dictionary<EvidenceSourceKind, SourceFieldValue>();
            foreach (var fe in group)
            {
                var sourceKind = ToComparisonSourceKind(fe.Source);
                if (sourceKind is null)
                {
                    continue; // e.g. FieldEvidenceSource.Barcode - no comparable value, see RunBarcodeAsync's own remarks.
                }

                valuesBySource[sourceKind.Value] = SourceFieldValue.Create(sourceKind.Value, fe.RawValue, fe.NormalizedValue, fe.Confidence);
            }

            if (valuesBySource.Count > 0)
            {
                fieldComparisons.Add(ComparisonEngine.Compare(group.Key, valuesBySource));
            }
        }

        var versionNumber = comparisonState.NextVersionNumber(sessionId);
        var crossComparison = CrossComparisonResult.Compute(
            sessionId, versionNumber, actorId, now, correlationId, fieldComparisons, NfcCorroboration.NotAvailable,
            comparisonState.AuditSink, comparisonState.VersionStore);

        foreach (var fieldName in crossComparison.FieldsRequiringReview)
        {
            var conflictingSequenceNumbers = fieldEvidence
                .Where(f => f.FieldName == fieldName && ToComparisonSourceKind(f.Source) is not null)
                .Select(f => f.SequenceNumber)
                .ToList();

            try
            {
                sessionStore.RaiseConflict(tenantId, sessionId, fieldName, conflictingSequenceNumbers, now);
            }
            catch (InvalidOperationException)
            {
                // Already raised and still unresolved from an earlier CheckAuthenticityAsync call
                // for this same session/field (e.g. a replay) - the conflict already exists and
                // the session is already in ManualReviewPending; nothing further to do here.
            }
        }

        var mrzCheck = BuildEngineCheckInput(fieldEvidence, FieldEvidenceSource.Mrz);
        var barcodeCheck = BuildEngineCheckInput(fieldEvidence, FieldEvidenceSource.Barcode);
        var ocrCheck = BuildEngineCheckInput(fieldEvidence, FieldEvidenceSource.Ocr);

        var snapshot = await authenticityGateway.ComputeFromEvidenceAsync(
            sessionId, mrzCheck, barcodeCheck, ocrCheck, crossComparison, actorId, correlationId, cancellationToken);

        return new EvidenceStepOutcome("success", snapshot.EvidenceCheckState, snapshot.Summary, snapshot.Confidence,
            new EvidenceProvenance("ocr", null, null));
    }

    /// <summary>
    /// BIO-UX-009-AC04: "no real biometric data" is this capability's own permanent, approved
    /// design - not a gap this story closes. No face/biometric engine is a declared dependency of
    /// this coordinator; this step stays a deterministic synthetic-fixture result, the
    /// <c>synthetic-fixture</c> provenance source Kemi's ruling (point 7) names for exactly this
    /// kind of permanently-simulated step.
    /// </summary>
    public async Task<EvidenceStepOutcome> CompareSelfieAsync(string sessionId, string actorId, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        RequireCaptureEvidence(tenantId, sessionId);

        return new EvidenceStepOutcome(
            "success", "authentic",
            "Portrait-to-live-selfie comparison is intentionally simulated (BIO-UX-009-AC04: no real biometric data is collected).",
            1.0, new EvidenceProvenance("synthetic-fixture", null, null));
    }

    private DocumentEvidence RequireCaptureEvidence(string tenantId, string sessionId)
    {
        DocumentSession session;
        IReadOnlyList<DocumentEvidence> evidence;
        try
        {
            session = sessionStore.GetSession(tenantId, sessionId);
            evidence = sessionStore.ListEvidence(tenantId, sessionId);
        }
        catch (DocumentSessionNotFoundException)
        {
            throw DocumentVerifyCoordinatorError.NoActiveSession();
        }

        _ = session;
        var capture = evidence.FirstOrDefault(e => e.Source == EvidenceSource.Capture);
        return capture ?? throw DocumentVerifyCoordinatorError.NoActiveSession();
    }

    private async Task TryProposePersonFieldChangeAsync(
        string tenantId, string sessionId, string? personField, DocumentFieldEvidence fieldEvidence, string now, CancellationToken cancellationToken)
    {
        if (personField is null)
        {
            return;
        }

        DocumentSession session;
        try
        {
            session = sessionStore.GetSession(tenantId, sessionId);
        }
        catch (DocumentSessionNotFoundException)
        {
            return;
        }

        if (session.PersonId is null)
        {
            // BIO-DOC-001-AC01's pre-person onboarding path - nothing to propose a PERSON-field
            // change for yet. See this type's own header remarks.
            return;
        }

        sessionStore.ProposePersonFieldChange(tenantId, sessionId, personField, fieldEvidence.NormalizedValue, fieldEvidence.SequenceNumber, now);
        await Task.CompletedTask;
    }

    private static string? MrzPersonField(MrzModel.MrzFieldKind kind) => kind switch
    {
        MrzModel.MrzFieldKind.DateOfBirth => "date_of_birth",
        MrzModel.MrzFieldKind.Sex => "sex",
        _ => null,
    };

    private static string? OcrPersonField(string ocrFieldName) => ocrFieldName switch
    {
        "Surname" => "surname",
        "GivenNames" => "given_names",
        "DateOfBirth" => "date_of_birth",
        _ => null,
    };

    private static EvidenceSourceKind? ToComparisonSourceKind(FieldEvidenceSource source) => source switch
    {
        FieldEvidenceSource.Mrz => EvidenceSourceKind.Mrz,
        FieldEvidenceSource.Ocr => EvidenceSourceKind.Visual,
        FieldEvidenceSource.Nfc => EvidenceSourceKind.Nfc,
        // FieldEvidenceSource.Barcode/Manual: no comparable VALUE at this layer - see
        // RunBarcodeAsync's own remarks.
        _ => null,
    };

    private static EngineCheckInput BuildEngineCheckInput(IReadOnlyList<DocumentFieldEvidence> fieldEvidence, FieldEvidenceSource source)
    {
        var matches = fieldEvidence.Where(f => f.Source == source).ToList();
        if (matches.Count == 0)
        {
            return new EngineCheckInput(EngineCheckOutcome.NotPerformed, $"{source} was not attempted for this session.");
        }

        var minConfidence = matches.Min(f => f.Confidence);
        return minConfidence >= OcrModel.TemplateOcrEngine.MinAcceptableConfidence
            ? new EngineCheckInput(EngineCheckOutcome.Passed, $"{source} extracted {matches.Count} field(s) above the confidence threshold.")
            : new EngineCheckInput(EngineCheckOutcome.Inconclusive, $"{source} extracted {matches.Count} field(s), at least one below the confidence threshold.");
    }

    private IReadOnlyDictionary<string, string> BuildObservedFieldsFromOcrEvidence(string tenantId, string sessionId)
    {
        var fieldEvidence = sessionStore.ListFieldEvidence(tenantId, sessionId).Where(f => f.Source == FieldEvidenceSource.Ocr);
        var observed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fe in fieldEvidence)
        {
            // "document_number" is this coordinator's own comparison alias for OCR's own
            // "PassportNumber" field (see NormalizeMrzFieldNameForComparison/RunOcrAsync) - restore
            // the pack's own field name here so IdentifyFromEvidenceAsync's observed-fields map
            // lines up with NigeriaDocumentPackV1.PassportEntry's own required field names.
            var fieldName = fe.FieldName == ComparableDocumentNumberField ? "PassportNumber" : fe.FieldName;
            observed[fieldName] = fe.NormalizedValue;
        }

        return observed;
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw DocumentVerifyCoordinatorError.TenantContextMissing();
}

/// <summary>Same <c>yyyy-MM-ddTHH:mm:ss.fffZ</c> UTC-instant shape every sibling feature's own
/// local clock helper in this epic uses (e.g. BIO-DOC-009's <c>AuthenticityAssessmentClock</c>).</summary>
internal static class UtcInstant
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static string Now() => DateTime.UtcNow.ToString(Format, System.Globalization.CultureInfo.InvariantCulture);
}
