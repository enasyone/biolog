namespace Biolog.Features.MrzEngine.Model;

/// <summary>The MRZ format keys this engine parses (BIO-DOC-002-AC02). TD1/TD2/TD3 are the ICAO
/// Doc 9303 standard sizes; MrvA/MrvB are the machine-readable-visa sizes, registered as
/// "configured visa formats" (see <see cref="MrzFormatRegistry"/>) rather than hard-coded
/// alongside the three ID/passport formats.</summary>
public enum MrzFormatKind
{
    Td1,
    Td2,
    Td3,
    MrvA,
    MrvB,
}

public enum MrzFieldKind
{
    DocumentCode,
    IssuingState,
    Name,
    DocumentNumber,
    Nationality,
    DateOfBirth,
    Sex,
    Expiry,
    PersonalNumber,
    OptionalData,
}

/// <summary>One field's position within an <see cref="MrzFormatDescriptor"/>'s lines. Column
/// positions are 0-based, per ICAO Doc 9303's own field-layout tables.</summary>
public sealed record MrzFieldSpec(MrzFieldKind Kind, int Line, int Start, int Length, bool HasCheckDigit)
{
    public int End => Start + Length;

    /// <summary>Column of the check-digit character immediately following this field - only
    /// meaningful when <see cref="HasCheckDigit"/> is true.</summary>
    public int CheckDigitColumn => Start + Length;
}

/// <summary>
/// The field layout for one MRZ format (BIO-DOC-002-AC02). <see cref="DocumentCodeDiscriminator"/>
/// resolves ambiguity when two formats share the same (line count, line length) geometry - e.g.
/// TD3 and MrvA are both 2x44 - by testing the document-code character actually read off line 0
/// (ICAO Doc 9303 Part 3 §4.9: the visa document code's first character is always <c>V</c>).
/// </summary>
public sealed record MrzFormatDescriptor(
    MrzFormatKind Kind,
    int LineCount,
    int LineLength,
    bool HasCompositeCheckDigit,
    Func<char, bool> DocumentCodeDiscriminator,
    IReadOnlyList<MrzFieldSpec> Fields);

/// <summary>
/// Registered MRZ formats, keyed by geometry (BIO-DOC-002-AC02: "TD1/TD2/TD3 and configured visa
/// formats are parsed"). TD1/TD2/TD3 are the three ICAO-standard sizes this engine always knows;
/// the two visa sizes are added via the same <see cref="Register"/> seam a later story would use
/// to add a further country-specific visa variant, rather than being distinguished from TD1-3 by
/// any special-cased code path - "configured" in the AC text means exactly this: format
/// recognition is data-driven from this table, not an enum-driven switch that only understands
/// three hard-coded shapes.
///
/// <para>
/// Scope note: MrvA/MrvB here reuse TD3/TD2's column geometry (both visa sizes really do share
/// that geometry per ICAO Doc 9303 Part 7) with <see cref="MrzFormatDescriptor.HasCompositeCheckDigit"/>
/// set to <c>false</c> (visas do not carry a final composite check digit) and a document-code
/// discriminator requiring <c>V</c>. A future story adding a country-specific visa field layout
/// that diverges further can register its own descriptor here without changing this engine's
/// detector, recognizer or parser.
/// </para>
/// </summary>
public static class MrzFormatRegistry
{
    private static readonly List<MrzFormatDescriptor> Formats = BuildDefaults();

    public static IReadOnlyList<MrzFormatDescriptor> All => Formats;

    /// <summary>Registers an additional format. Not used to register TD1/TD2/TD3/MrvA/MrvB
    /// themselves (those are always present) - the seam a later story adds a further configured
    /// visa or national-ID variant through.</summary>
    public static void Register(MrzFormatDescriptor descriptor) => Formats.Add(descriptor);

    /// <summary>Every registered format whose geometry matches (BIO-DOC-002-AC01's detector
    /// output: line count + line length). May return more than one candidate (TD3/MrvA share
    /// 2x44; TD2/MrvB share 2x36) - disambiguated by <see cref="ResolveByDocumentCode"/>.</summary>
    public static IReadOnlyList<MrzFormatDescriptor> ResolveByShape(int lineCount, int lineLength) =>
        Formats.Where(f => f.LineCount == lineCount && f.LineLength == lineLength).ToList();

    /// <summary>Narrows shape candidates by the document-code character actually read at line 0,
    /// column 0. Returns null if zero or more than one candidate matches - an ambiguous or
    /// unrecognized document code is an AC05 "requires review" signal, never a guessed pick.</summary>
    public static MrzFormatDescriptor? ResolveByDocumentCode(IReadOnlyList<MrzFormatDescriptor> candidates, char documentCodeFirstChar)
    {
        var matches = candidates.Where(f => f.DocumentCodeDiscriminator(documentCodeFirstChar)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static List<MrzFormatDescriptor> BuildDefaults() =>
    [
        new MrzFormatDescriptor(
            MrzFormatKind.Td3,
            LineCount: 2,
            LineLength: 44,
            HasCompositeCheckDigit: true,
            DocumentCodeDiscriminator: c => c != 'V',
            Fields:
            [
                new MrzFieldSpec(MrzFieldKind.DocumentCode, Line: 0, Start: 0, Length: 2, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.IssuingState, Line: 0, Start: 2, Length: 3, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.Name, Line: 0, Start: 5, Length: 39, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.DocumentNumber, Line: 1, Start: 0, Length: 9, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.Nationality, Line: 1, Start: 10, Length: 3, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.DateOfBirth, Line: 1, Start: 13, Length: 6, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.Sex, Line: 1, Start: 20, Length: 1, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.Expiry, Line: 1, Start: 21, Length: 6, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.PersonalNumber, Line: 1, Start: 28, Length: 14, HasCheckDigit: true),
            ]),

        new MrzFormatDescriptor(
            MrzFormatKind.MrvA,
            LineCount: 2,
            LineLength: 44,
            HasCompositeCheckDigit: false,
            DocumentCodeDiscriminator: c => c == 'V',
            Fields:
            [
                new MrzFieldSpec(MrzFieldKind.DocumentCode, Line: 0, Start: 0, Length: 2, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.IssuingState, Line: 0, Start: 2, Length: 3, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.Name, Line: 0, Start: 5, Length: 39, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.DocumentNumber, Line: 1, Start: 0, Length: 9, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.Nationality, Line: 1, Start: 10, Length: 3, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.DateOfBirth, Line: 1, Start: 13, Length: 6, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.Sex, Line: 1, Start: 20, Length: 1, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.Expiry, Line: 1, Start: 21, Length: 6, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.OptionalData, Line: 1, Start: 28, Length: 16, HasCheckDigit: false),
            ]),

        new MrzFormatDescriptor(
            MrzFormatKind.Td2,
            LineCount: 2,
            LineLength: 36,
            HasCompositeCheckDigit: true,
            DocumentCodeDiscriminator: c => c != 'V',
            Fields:
            [
                new MrzFieldSpec(MrzFieldKind.DocumentCode, Line: 0, Start: 0, Length: 2, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.IssuingState, Line: 0, Start: 2, Length: 3, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.Name, Line: 0, Start: 5, Length: 31, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.DocumentNumber, Line: 1, Start: 0, Length: 9, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.Nationality, Line: 1, Start: 10, Length: 3, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.DateOfBirth, Line: 1, Start: 13, Length: 6, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.Sex, Line: 1, Start: 20, Length: 1, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.Expiry, Line: 1, Start: 21, Length: 6, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.OptionalData, Line: 1, Start: 28, Length: 7, HasCheckDigit: false),
            ]),

        new MrzFormatDescriptor(
            MrzFormatKind.MrvB,
            LineCount: 2,
            LineLength: 36,
            HasCompositeCheckDigit: false,
            DocumentCodeDiscriminator: c => c == 'V',
            Fields:
            [
                new MrzFieldSpec(MrzFieldKind.DocumentCode, Line: 0, Start: 0, Length: 2, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.IssuingState, Line: 0, Start: 2, Length: 3, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.Name, Line: 0, Start: 5, Length: 31, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.DocumentNumber, Line: 1, Start: 0, Length: 9, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.Nationality, Line: 1, Start: 10, Length: 3, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.DateOfBirth, Line: 1, Start: 13, Length: 6, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.Sex, Line: 1, Start: 20, Length: 1, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.Expiry, Line: 1, Start: 21, Length: 6, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.OptionalData, Line: 1, Start: 28, Length: 8, HasCheckDigit: false),
            ]),

        new MrzFormatDescriptor(
            MrzFormatKind.Td1,
            LineCount: 3,
            LineLength: 30,
            HasCompositeCheckDigit: true,
            DocumentCodeDiscriminator: c => c != 'V',
            Fields:
            [
                new MrzFieldSpec(MrzFieldKind.DocumentCode, Line: 0, Start: 0, Length: 2, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.IssuingState, Line: 0, Start: 2, Length: 3, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.DocumentNumber, Line: 0, Start: 5, Length: 9, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.OptionalData, Line: 0, Start: 15, Length: 15, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.DateOfBirth, Line: 1, Start: 0, Length: 6, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.Sex, Line: 1, Start: 7, Length: 1, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.Expiry, Line: 1, Start: 8, Length: 6, HasCheckDigit: true),
                new MrzFieldSpec(MrzFieldKind.Nationality, Line: 1, Start: 15, Length: 3, HasCheckDigit: false),
                // Positions 18-28 of line 1 (11 chars); column 29 is the composite check digit,
                // computed below over line0[5..30) + line1[0..7) + line1[8..15) only - this
                // second optional-data span (like nationality) is NOT part of that composite
                // input per ICAO Doc 9303 Part 5, it is preserved as its own field (AC04) but not
                // independently check-digit-validated (TD1 gives it none of its own).
                new MrzFieldSpec(MrzFieldKind.OptionalData, Line: 1, Start: 18, Length: 11, HasCheckDigit: false),
                new MrzFieldSpec(MrzFieldKind.Name, Line: 2, Start: 0, Length: 30, HasCheckDigit: false),
            ]),
    ];
}
