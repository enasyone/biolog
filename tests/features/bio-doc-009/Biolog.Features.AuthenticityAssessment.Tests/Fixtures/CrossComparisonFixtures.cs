using Biolog.Features.EvidenceComparison.Audit;
using Biolog.Features.EvidenceComparison.Model;

namespace Biolog.Features.AuthenticityAssessment.Tests.Fixtures;

/// <summary>Small helpers for building real BIO-DOC-008 <see cref="CrossComparisonResult"/>
/// instances in tests, entirely through BIO-DOC-008's own public surface (never a hand-built
/// fake) - so a test proves this project's policy logic against the actual sibling type, not a
/// stand-in for it.</summary>
internal static class CrossComparisonFixtures
{
    public const string SampleSessionId = "test-session-1";
    public const string SampleActorId = "test-actor";
    public const string SampleTimestamp = "2026-09-23T12:00:00.000Z";
    public const string SampleCorrelationId = "test-correlation-1";

    private static SourceFieldValue Value(EvidenceSourceKind source, string normalized, double confidence = 0.9) =>
        SourceFieldValue.Create(source, normalized, normalized, confidence);

    public static CrossComparisonResult Compute(
        IReadOnlyList<FieldComparison> fieldComparisons,
        NfcCorroboration? nfcCorroboration = null,
        int versionNumber = 1,
        string documentSessionId = SampleSessionId,
        IVersionStore? versionStore = null,
        IEvidenceComparisonAuditSink? auditSink = null) =>
        CrossComparisonResult.Compute(
            documentSessionId, versionNumber, SampleActorId, SampleTimestamp, SampleCorrelationId,
            fieldComparisons, nfcCorroboration ?? NfcCorroboration.NotAvailable,
            auditSink ?? new InMemoryEvidenceComparisonAuditSink(), versionStore ?? new InMemoryVersionStore());

    /// <summary>A cross-comparison where every supplied field has two agreeing independent
    /// sources - reaches <c>CrossSourceFieldComparison</c> Passed.</summary>
    public static CrossComparisonResult AllFieldsAgree(string documentSessionId = SampleSessionId, int versionNumber = 1) => Compute(
        [
            ComparisonEngine.Compare("document_number", new Dictionary<EvidenceSourceKind, SourceFieldValue>
            {
                [EvidenceSourceKind.Mrz] = Value(EvidenceSourceKind.Mrz, "A1234567"),
                [EvidenceSourceKind.Barcode] = Value(EvidenceSourceKind.Barcode, "A1234567"),
            }),
        ],
        documentSessionId: documentSessionId, versionNumber: versionNumber);

    /// <summary>A cross-comparison where a field has two DISAGREEING independent sources -
    /// reaches <c>CrossSourceFieldComparison</c> Failed.</summary>
    public static CrossComparisonResult FieldsDisagree(string documentSessionId = SampleSessionId, int versionNumber = 1) => Compute(
        [
            ComparisonEngine.Compare("date_of_birth", new Dictionary<EvidenceSourceKind, SourceFieldValue>
            {
                [EvidenceSourceKind.Mrz] = Value(EvidenceSourceKind.Mrz, "1990-05-14"),
                [EvidenceSourceKind.Visual] = Value(EvidenceSourceKind.Visual, "1991-05-14"),
            }),
        ],
        documentSessionId: documentSessionId, versionNumber: versionNumber);

    /// <summary>A cross-comparison where the only field supplied has a SINGLE source (e.g.
    /// barcode alone) - reaches <c>CrossSourceFieldComparison</c> NotPerformed (BIO-DOC-008's own
    /// "fewer than two sources is Unknown" rule). This is exactly BIO-DOC-009-AC02's own
    /// "barcode decode alone" shape.</summary>
    public static CrossComparisonResult SingleSourceOnly(string documentSessionId = SampleSessionId, int versionNumber = 1) => Compute(
        [
            ComparisonEngine.Compare("document_number", new Dictionary<EvidenceSourceKind, SourceFieldValue>
            {
                [EvidenceSourceKind.Barcode] = Value(EvidenceSourceKind.Barcode, "B7654321"),
            }),
        ],
        documentSessionId: documentSessionId, versionNumber: versionNumber);

    /// <summary>No fields supplied at all - reaches <c>CrossSourceFieldComparison</c>
    /// NotPerformed via the empty-list branch.</summary>
    public static CrossComparisonResult NoFields(string documentSessionId = SampleSessionId, int versionNumber = 1) => Compute(
        [], documentSessionId: documentSessionId, versionNumber: versionNumber);
}
