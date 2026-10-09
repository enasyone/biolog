namespace Biolog.Features.NfcReader.Model;

/// <summary>
/// ICAO 9303 Part 10's Logical Data Structure (LDS1) elements a chip's application can expose
/// (BIO-DOC-007-AC02): <c>Com</c> declares which data groups are actually present, <c>Dg1</c>
/// through <c>Dg16</c> are the standard data groups (Dg1 = MRZ, Dg2 = encoded face, Dg3 = encoded
/// fingerprint(s) - access-restricted under Extended Access Control, not readable under Basic
/// Access Control alone - Dg14 = security infos, Dg15 = Active Authentication public key, and so
/// on), and <c>Sod</c> is the Document Security Object passive authentication is evaluated
/// against.
/// </summary>
public enum ChipDataGroupId
{
    Com,
    Dg1,
    Dg2,
    Dg3,
    Dg4,
    Dg5,
    Dg6,
    Dg7,
    Dg8,
    Dg9,
    Dg10,
    Dg11,
    Dg12,
    Dg13,
    Dg14,
    Dg15,
    Dg16,
    Sod,
}
