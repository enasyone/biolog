namespace Biolog.Features.DocumentVerify.Model;

/// <summary>
/// Data-sensitivity classification carried by every evidence record (BIO-DOC-001-AC03), so a
/// consumer that later builds an ADR-0003 audit/domain-event envelope around this evidence can
/// read a classification off it rather than re-deriving one. Named and ordered to match
/// ADR-0003 Decision 3's five levels; <c>Secret</c> is deliberately omitted here because ADR-0003
/// requires a secret-classified value to never be carried in any record that could be projected
/// into an envelope, and nothing this domain model stores (hashes, bounding boxes, normalized
/// field text) is a biometric template, key or credential - the kind of value ADR-0003 reserves
/// that level for. A future engine that would otherwise need it does not extend this enum; it
/// keeps that value out of this model entirely, the same way ADR-0003 keeps it out of envelopes.
/// </summary>
public enum EvidenceClassification
{
    Public,
    Internal,
    Personal,
    Sensitive,
}
