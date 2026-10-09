namespace Biolog.Features.DocumentVerifyCoordination;

/// <summary>
/// Kemi's BIO-OPS-005 ruling, point 7: the exact fields <c>web/features/documents/evidence.ts</c>'s
/// now-required <c>EvidenceStepResult</c>/<c>LiveStepBody</c> carry - this is the plain shape the
/// HTTP layer serializes directly, one per step.
/// </summary>
public sealed record EvidenceStepOutcome(string Outcome, string State, string Message, double Confidence, EvidenceProvenance Provenance);

/// <summary>
/// <paramref name="Source"/> is one of the exact six wire values point 7 names:
/// <c>mrz|barcode|ocr|nfc|synthetic-fixture|unavailable</c>. <paramref name="EvidenceSequenceNumber"/>/
/// <paramref name="FieldEvidenceSequenceNumbers"/> reuse BIO-DOC-001's own
/// <see cref="Biolog.Features.DocumentVerify.Model.DocumentEvidence.SequenceNumber"/>/
/// <see cref="Biolog.Features.DocumentVerify.Model.DocumentFieldEvidence.SequenceNumber"/>
/// identity directly (point 7: "no new canonical id invented").
/// </summary>
public sealed record EvidenceProvenance(string Source, int? EvidenceSequenceNumber, IReadOnlyList<int>? FieldEvidenceSequenceNumbers);
