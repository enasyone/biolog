namespace Biolog.Features.DocumentClassifier.Tests.Fixtures;

/// <summary>Own local copy of BIO-DOC-002's identically named fixture, trimmed to the one sample
/// this story's tests need: ICAO Doc 9303 Part 4 Appendix B's published TD3 worked example
/// (issuing state "UTO" - the fictional ICAO test country code), a literal hand-verified string
/// used as an independent oracle rather than built by this fixture.</summary>
public static class MrzSampleData
{
    public const string Td3Line1 = "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<";
    public const string Td3Line2 = "L898902C36UTO7408122F1204159ZE184226B<<<<<10";
}
