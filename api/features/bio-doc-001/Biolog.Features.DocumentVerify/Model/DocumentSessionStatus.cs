namespace Biolog.Features.DocumentVerify.Model;

/// <summary>
/// Lifecycle of a <see cref="DocumentSession"/> (BIO-DOC-001-AC01, AC05).
/// </summary>
public enum DocumentSessionStatus
{
    /// <summary>Session created; may or may not yet be linked to a person (AC01).</summary>
    Open,

    /// <summary>Session is linked to exactly one person.</summary>
    PersonLinked,

    /// <summary>A conflict or other exception has been raised against this session and a
    /// human must resolve it before the session can close (AC05).</summary>
    ManualReviewPending,

    /// <summary>Terminal: no further evidence is accepted.</summary>
    Closed,
}
