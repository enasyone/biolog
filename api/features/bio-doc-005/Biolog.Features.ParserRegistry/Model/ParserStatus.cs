namespace Biolog.Features.ParserRegistry.Model;

/// <summary>A parser version's workflow state (BIO-DOC-005-AC02: "versioned, tested and published
/// through approval"). Only ever moves forward: Draft -&gt; Tested -&gt; Published. A version that
/// failed testing stays Draft (revisable, retestable); once Tested or Published, its content is
/// frozen - any further change starts a new version (BIO-DOC-005-AC04), never mutates this one.</summary>
public enum ParserStatus
{
    Draft,
    Tested,
    Published,
}
