using Biolog.Features.DocumentVerify.Model;
using Biolog.Features.MrzEngine.Model;
using Biolog.Features.MrzEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.MrzEngine.Tests;

/// <summary>Developer tests for BIO-DOC-002-AC04's concrete proof: an MRZ read adapts directly
/// into BIO-DOC-001's session-scoped <see cref="DocumentFieldEvidence"/> via the
/// <see cref="FieldEvidenceSource.Mrz"/> member that story already reserved for this one.</summary>
public sealed class MrzFieldReadAdapterTests
{
    [Fact]
    public void AC04_a_parsed_field_adapts_into_a_valid_document_field_evidence_record()
    {
        var canvas = new SyntheticRasterCanvas(width: 270, height: 40);
        canvas.DrawMrzLines([MrzSampleData.Td3Line1, MrzSampleData.Td3Line2], left: 3, top: 15);
        var outcome = MrzReader.Read(canvas.ToRaster());
        var documentNumber = outcome.Fields.Single(f => f.FieldName == "document_number");

        var evidence = documentNumber.ToDocumentFieldEvidence(
            tenantId: "ten_test-tenant",
            documentSessionId: "dcs_1",
            sequenceNumber: 1,
            sourceEvidenceSequenceNumber: 1,
            extractedAt: "2026-09-22T12:00:00.000Z",
            classification: EvidenceClassification.Personal);

        Assert.Equal(FieldEvidenceSource.Mrz, evidence.Source);
        Assert.Equal("document_number", evidence.FieldName);
        Assert.Equal("L898902C3", evidence.RawValue);
        Assert.Equal(documentNumber.NormalizedValue, evidence.NormalizedValue);
        Assert.Equal(documentNumber.Confidence, evidence.Confidence);
        Assert.Equal(documentNumber.BoundingBox, evidence.BoundingBox);
    }
}
