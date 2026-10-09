using Biolog.Features.MrzEngine.Model;

namespace Biolog.Features.MrzEngine.Tests.Fixtures;

/// <summary>
/// Known-valid MRZ line sets for tests. TD3 is ICAO Doc 9303 Part 4 Appendix B's own published
/// worked example (the fictional "ERIKSSON, ANNA MARIA" passport) - a literal, hand-verified
/// string, not built by this fixture, so it stays an independent oracle for
/// <see cref="MrzCheckDigit"/>'s correctness. TD1/TD2/MRV-A/MRV-B samples ARE built
/// programmatically from field values using <see cref="MrzCheckDigit.Compute"/>, which avoids the
/// far more error-prone alternative of hand-counting '&lt;' fillers in a 30-44 character literal
/// - line-length correctness is asserted, not assumed, by every factory below.
/// </summary>
public static class MrzSampleData
{
    /// <summary>ICAO's published TD3 worked example. Line1: doc type/state/name. Line2: doc
    /// number+check, nationality, DOB+check, sex, expiry+check, personal number+check, composite.</summary>
    public const string Td3Line1 = "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<";
    public const string Td3Line2 = "L898902C36UTO7408122F1204159ZE184226B<<<<<10";

    public static (string Line1, string Line2) BuildTd2(
        string documentCode = "I<",
        string issuingState = "UTO",
        string surname = "ERIKSSON",
        string givenNames = "ANNA MARIA",
        string documentNumber = "D23145890",
        string nationality = "UTO",
        string dateOfBirth = "740812",
        string sex = "F",
        string expiry = "120415",
        string optionalData = "")
    {
        var line1 = Pad(documentCode + issuingState + FormatName(surname, givenNames, 31), 36);

        var docNumCheck = MrzCheckDigit.Compute(documentNumber);
        var dobCheck = MrzCheckDigit.Compute(dateOfBirth);
        var expiryCheck = MrzCheckDigit.Compute(expiry);
        var optional = Pad(optionalData, 7);

        var beforeComposite = $"{documentNumber}{docNumCheck}{nationality}{dateOfBirth}{dobCheck}{sex}{expiry}{expiryCheck}{optional}";
        var composite = MrzCheckDigit.Compute($"{documentNumber}{docNumCheck}{dateOfBirth}{dobCheck}{expiry}{expiryCheck}{optional}");
        var line2 = beforeComposite + composite;

        AssertLength(line1, 36, nameof(line1));
        AssertLength(line2, 36, nameof(line2));
        return (line1, line2);
    }

    public static (string Line1, string Line2, string Line3) BuildTd1(
        string documentCode = "I<",
        string issuingState = "UTO",
        string documentNumber = "D23145890",
        string optionalData1 = "",
        string nationality = "UTO",
        string dateOfBirth = "740812",
        string sex = "F",
        string expiry = "120415",
        string optionalData2 = "",
        string surname = "ERIKSSON",
        string givenNames = "ANNA MARIA")
    {
        var docNumCheck = MrzCheckDigit.Compute(documentNumber);
        var optional1 = Pad(optionalData1, 15);
        var line1 = $"{documentCode}{issuingState}{documentNumber}{docNumCheck}{optional1}";

        var dobCheck = MrzCheckDigit.Compute(dateOfBirth);
        var expiryCheck = MrzCheckDigit.Compute(expiry);
        var optional2 = Pad(optionalData2, 11);
        var compositeInput = $"{documentNumber}{docNumCheck}{optional1}{dateOfBirth}{dobCheck}{expiry}{expiryCheck}";
        var composite = MrzCheckDigit.Compute(compositeInput);
        var line2 = $"{dateOfBirth}{dobCheck}{sex}{expiry}{expiryCheck}{nationality}{optional2}{composite}";

        var line3 = Pad(FormatName(surname, givenNames, 30), 30);

        AssertLength(line1, 30, nameof(line1));
        AssertLength(line2, 30, nameof(line2));
        AssertLength(line3, 30, nameof(line3));
        return (line1, line2, line3);
    }

    public static (string Line1, string Line2) BuildMrvA(
        string issuingState = "UTO",
        string surname = "ERIKSSON",
        string givenNames = "ANNA MARIA",
        string documentNumber = "D23145890",
        string nationality = "UTO",
        string dateOfBirth = "740812",
        string sex = "F",
        string expiry = "120415",
        string optionalData = "")
    {
        var line1 = Pad("V<" + issuingState + FormatName(surname, givenNames, 39), 44);
        var docNumCheck = MrzCheckDigit.Compute(documentNumber);
        var dobCheck = MrzCheckDigit.Compute(dateOfBirth);
        var expiryCheck = MrzCheckDigit.Compute(expiry);
        var optional = Pad(optionalData, 16);
        var line2 = $"{documentNumber}{docNumCheck}{nationality}{dateOfBirth}{dobCheck}{sex}{expiry}{expiryCheck}{optional}";

        AssertLength(line1, 44, nameof(line1));
        AssertLength(line2, 44, nameof(line2));
        return (line1, line2);
    }

    private static string FormatName(string surname, string givenNames, int fieldLength)
    {
        var raw = surname.Replace(' ', '<') + "<<" + givenNames.Replace(' ', '<');
        return Pad(raw, fieldLength);
    }

    private static string Pad(string value, int length)
    {
        if (value.Length > length)
        {
            throw new ArgumentException($"'{value}' ({value.Length} chars) exceeds field length {length}");
        }

        return value.PadRight(length, '<');
    }

    private static void AssertLength(string value, int expected, string name)
    {
        if (value.Length != expected)
        {
            throw new InvalidOperationException($"{name} built at length {value.Length}, expected {expected}: '{value}'");
        }
    }
}
