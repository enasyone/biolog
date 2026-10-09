namespace Biolog.Features.CardTemplates.Domain;

/// <summary>BIO-CRD-008: "Draft -&gt; Validation -&gt; Approval -&gt; Published -&gt; Retired lifecycle with immutable published versions."</summary>
public enum CardTemplateStatus
{
    Draft,
    Validation,
    Approval,
    Published,
    Retired,
}

public static class CardTemplateStatusExtensions
{
    public static string ToWireString(this CardTemplateStatus status) => status switch
    {
        CardTemplateStatus.Draft => "draft",
        CardTemplateStatus.Validation => "validation",
        CardTemplateStatus.Approval => "approval",
        CardTemplateStatus.Published => "published",
        CardTemplateStatus.Retired => "retired",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    /// <summary>AC01: once true, <see cref="CardTemplate.Edit"/> always refuses - the only way to change content is <see cref="CardTemplate.CreateNextDraftVersion"/> (AC02).</summary>
    public static bool IsImmutable(this CardTemplateStatus status) =>
        status is CardTemplateStatus.Published or CardTemplateStatus.Retired;
}
