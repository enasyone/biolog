namespace Biolog.Features.CardTypography.Domain;

/// <summary>This feature's domain error, shaped identically to every sibling story's so it duck-types cleanly onto <see cref="Api.ProblemResponse"/>'s builder (ADR-0004 §2.1).</summary>
public sealed class CardTypographyError(string message, string code, int status, IReadOnlyDictionary<string, object?>? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, object?> Detail { get; } = detail ?? new Dictionary<string, object?>();

    public static CardTypographyError UnknownPreset(string kind) => new(
        $"unknown watermark preset: {kind}", "watermark_preset_not_found", 404,
        new Dictionary<string, object?> { ["kind"] = kind });
}
