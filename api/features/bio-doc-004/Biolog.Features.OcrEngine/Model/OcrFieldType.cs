namespace Biolog.Features.OcrEngine.Model;

/// <summary>The field semantics a template can assign to a region (BIO-DOC-004-AC01). Generic OCR
/// (BIO-DOC-004-AC02) never assigns one of these - that is precisely the distinction between the
/// two engines this story builds.</summary>
public enum OcrFieldType
{
    Surname,
    GivenNames,
    DateOfBirth,
    Expiry,
    DocumentNumber,
    Address,
    Sex,
    Nationality,
    FreeText,
}
