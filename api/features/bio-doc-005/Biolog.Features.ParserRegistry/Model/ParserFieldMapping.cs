namespace Biolog.Features.ParserRegistry.Model;

/// <summary>One field a parser extracts from a raw decoded barcode payload (BIO-DOC-005-AC03):
/// a fixed byte offset/length slice - the positional encoding legacy/external credential formats
/// (e.g. AAMVA-style PDF417 driver's licences) actually use.</summary>
public sealed record ParserFieldMapping(string FieldName, int ByteOffset, int Length)
{
    public static ParserFieldMapping Create(string fieldName, int byteOffset, int length)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("must be a non-empty string", nameof(fieldName));
        }
        if (byteOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "must be >= 0");
        }
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "must be > 0");
        }

        return new ParserFieldMapping(fieldName, byteOffset, length);
    }
}

/// <summary>One validation rule attached to a mapped field by name (BIO-DOC-005-AC03) - kept
/// separate from <see cref="ParserFieldMapping"/> rather than folded into it, matching the AC's
/// own two-part wording ("field mapping and validation").</summary>
public sealed record ParserValidationRule(string FieldName, bool Required, string? Pattern)
{
    public static ParserValidationRule Create(string fieldName, bool required, string? pattern = null)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("must be a non-empty string", nameof(fieldName));
        }
        if (pattern is not null)
        {
            _ = new System.Text.RegularExpressions.Regex(pattern); // throws on an invalid pattern up front, not at parse time.
        }

        return new ParserValidationRule(fieldName, required, pattern);
    }
}
