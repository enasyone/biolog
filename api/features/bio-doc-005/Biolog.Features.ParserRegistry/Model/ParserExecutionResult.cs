namespace Biolog.Features.ParserRegistry.Model;

/// <summary>One mapped field's outcome when a published parser version is applied to a raw
/// payload (BIO-DOC-005-AC03). Locked down the same way as every other trust-bearing result type
/// in this codebase's document-intelligence stories.</summary>
public sealed record ParserFieldResult
{
    public string FieldName { get; internal init; } = null!;
    public string RawValue { get; internal init; } = null!;
    public bool Valid { get; internal init; }
    public string? FailureReason { get; internal init; }

    internal ParserFieldResult(string fieldName, string rawValue, bool valid, string? failureReason)
    {
        FieldName = fieldName;
        RawValue = rawValue;
        Valid = valid;
        FailureReason = failureReason;
    }
}

/// <summary>The full result of applying one published parser version to one raw payload
/// (BIO-DOC-005-AC03). <see cref="Success"/> is true only if every mapped field validated
/// cleanly.</summary>
public sealed record ParserExecutionResult
{
    public IReadOnlyList<ParserFieldResult> Fields { get; internal init; } = [];
    public bool Success { get; internal init; }

    internal ParserExecutionResult(IReadOnlyList<ParserFieldResult> fields, bool success)
    {
        Fields = fields;
        Success = success;
    }
}
