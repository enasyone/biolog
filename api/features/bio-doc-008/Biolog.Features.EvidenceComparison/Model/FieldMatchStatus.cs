namespace Biolog.Features.EvidenceComparison.Model;

/// <summary>BIO-DOC-008-AC01's exact three states: "match/mismatch/unknown".</summary>
public enum FieldMatchStatus
{
    /// <summary>Fewer than two sources supplied a value for this field - there is nothing to
    /// compare, never silently reported as a match.</summary>
    Unknown,

    Match,
    Mismatch,
}
