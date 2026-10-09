using Biolog.Features.DocumentVerify.Model;
using Xunit;

namespace Biolog.Features.DocumentVerify.Tests;

/// <summary>Developer tests for BIO-DOC-001-AC02 ("source images are immutable evidence objects
/// with hashes and page/side metadata").</summary>
public sealed class DocumentEvidenceTests
{
    private const string TenantId = "ten_test-tenant";
    private const string SessionId = "dcs_1";
    private static readonly string ValidHash = new('a', 64);
    private const string CapturedAt = "2026-09-21T12:00:00.000Z";
    private const string ReceivedAt = "2026-09-21T12:00:01.000Z";

    private static DocumentEvidence Sample() => DocumentEvidence.Create(
        TenantId, SessionId, sequenceNumber: 1, ValidHash, "image/jpeg",
        pageNumber: 1, DocumentSide.Front, EvidenceSource.Capture, CapturedAt, ReceivedAt,
        EvidenceClassification.Personal);

    [Fact]
    public void AC02_preserves_hash_and_page_side_metadata()
    {
        var evidence = Sample();

        Assert.Equal(ValidHash, evidence.Sha256Hash);
        Assert.Equal(1, evidence.PageNumber);
        Assert.Equal(DocumentSide.Front, evidence.Side);
        Assert.Equal(EvidenceSource.Capture, evidence.Source);
    }

    [Fact]
    public void AC02_records_device_claim_and_server_authoritative_receipt_time_separately()
    {
        var evidence = Sample();

        Assert.Equal(CapturedAt, evidence.CapturedAt);
        Assert.Equal(ReceivedAt, evidence.ReceivedAt);
    }

    [Fact]
    public void AC02_is_immutable_a_correction_is_an_independently_created_record_not_a_mutation()
    {
        // QA-03 (Chioma acceptance audit): every property's init accessor is now `private`, so a
        // "correction" can no longer be expressed as `original with { ... }` from outside this
        // type at all - it must go through Create again, validated the same as any other evidence.
        var original = Sample();

        var repaged = DocumentEvidence.Create(
            TenantId, SessionId, sequenceNumber: 1, ValidHash, "image/jpeg",
            pageNumber: 2, DocumentSide.Front, EvidenceSource.Capture, CapturedAt, ReceivedAt,
            EvidenceClassification.Personal);

        Assert.Equal(1, original.PageNumber);
        Assert.Equal(2, repaged.PageNumber);
        Assert.NotSame(original, repaged);
    }

    [Fact]
    public void nfc_side_is_recorded_explicitly_rather_than_guessed_for_a_chip_read()
    {
        var evidence = DocumentEvidence.Create(
            TenantId, SessionId, 1, ValidHash, "application/octet-stream",
            pageNumber: 1, DocumentSide.Nfc, EvidenceSource.NfcRead, CapturedAt, ReceivedAt,
            EvidenceClassification.Sensitive);

        Assert.Equal(DocumentSide.Nfc, evidence.Side);
        Assert.Equal(EvidenceSource.NfcRead, evidence.Source);
    }

    public static IEnumerable<object[]> MalformedHashes()
    {
        yield return new object[] { "" };
        yield return new object[] { "too-short" };
        yield return new object[] { new string('A', 64) }; // uppercase hex is rejected
        yield return new object[] { new string('g', 64) }; // not hex at all
    }

    [Theory]
    [MemberData(nameof(MalformedHashes))]
    public void rejects_a_malformed_hash(string badHash)
    {
        Assert.Throws<ArgumentException>(() => DocumentEvidence.Create(
            TenantId, SessionId, 1, badHash, "image/jpeg", 1, DocumentSide.Front,
            EvidenceSource.Upload, CapturedAt, ReceivedAt, EvidenceClassification.Personal));
    }

    [Fact]
    public void rejects_a_non_positive_sequence_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentEvidence.Create(
            TenantId, SessionId, 0, ValidHash, "image/jpeg", 1, DocumentSide.Front,
            EvidenceSource.Upload, CapturedAt, ReceivedAt, EvidenceClassification.Personal));
    }

    [Fact]
    public void rejects_a_non_positive_page_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentEvidence.Create(
            TenantId, SessionId, 1, ValidHash, "image/jpeg", 0, DocumentSide.Front,
            EvidenceSource.Upload, CapturedAt, ReceivedAt, EvidenceClassification.Personal));
    }

    // Found alongside QA-03 while closing it: Create() validated everything except that its enum
    // parameters were actually declared members - a caller could not reach this through the
    // Model.DocumentSide/EvidenceSource/EvidenceClassification enums themselves (there is no
    // public way to name an undefined member), but an explicit numeric cast bypasses that, and
    // Create() had no check that would have caught it even before QA-03 was found.
    [Fact]
    public void rejects_an_undefined_side_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentEvidence.Create(
            TenantId, SessionId, 1, ValidHash, "image/jpeg", 1, (DocumentSide)999,
            EvidenceSource.Upload, CapturedAt, ReceivedAt, EvidenceClassification.Personal));
    }

    [Fact]
    public void rejects_an_undefined_source_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentEvidence.Create(
            TenantId, SessionId, 1, ValidHash, "image/jpeg", 1, DocumentSide.Front,
            (EvidenceSource)999, CapturedAt, ReceivedAt, EvidenceClassification.Personal));
    }

    [Fact]
    public void rejects_an_undefined_classification_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentEvidence.Create(
            TenantId, SessionId, 1, ValidHash, "image/jpeg", 1, DocumentSide.Front,
            EvidenceSource.Upload, CapturedAt, ReceivedAt, (EvidenceClassification)999));
    }
}
