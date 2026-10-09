using Biolog.Features.DocumentVerify.Model;

var invalidImage = new DocumentEvidence
{
    TenantId = "",
    DocumentSessionId = "",
    SequenceNumber = 0,
    Sha256Hash = "not-a-hash",
    MimeType = "",
    PageNumber = 0,
    Side = (DocumentSide)999,
    Source = (EvidenceSource)999,
    CapturedAt = "",
    ReceivedAt = "",
    Classification = (EvidenceClassification)999,
};

var invalidField = new DocumentFieldEvidence
{
    TenantId = "",
    DocumentSessionId = "",
    SequenceNumber = 0,
    SourceEvidenceSequenceNumber = 0,
    FieldName = "",
    RawValue = "raw",
    NormalizedValue = "normalized",
    Confidence = double.NaN,
    Source = (FieldEvidenceSource)999,
    BoundingBox = new BoundingBox { X = -1, Y = double.NaN, Width = 0, Height = 2 },
    ExtractedAt = "",
    Classification = (EvidenceClassification)999,
};

Console.WriteLine($"INVALID IMAGE CONSTRUCTED: hash={invalidImage.Sha256Hash}, page={invalidImage.PageNumber}");
Console.WriteLine($"INVALID FIELD CONSTRUCTED: confidence={invalidField.Confidence}, box.x={invalidField.BoundingBox!.X}");
return 1;
