namespace Biolog.Features.DocumentClassifier.Model;

/// <summary>
/// The side/page a classified document was captured from (BIO-DOC-006-AC04). This is this
/// feature's own small vocabulary, deliberately NOT a reference to BIO-DOC-001's
/// <c>DocumentVerify.Model.DocumentSide</c> - BIO-DOC-001 is not a declared dependency of this
/// story (its own DEPENDENCIES line names only BIO-DOC-002/003/004), matching the precedent
/// BIO-DOC-003/004 already set for "define a small local type rather than reach for one from an
/// undeclared dependency."
/// </summary>
public enum DocumentSide
{
    Front,
    Back,

    /// <summary>Neither could be determined from the evidence supplied, nor was it asserted by
    /// the caller - never guessed (AC02/AC05's "never silently pick" discipline extended to
    /// side detection).</summary>
    Unknown,
}
