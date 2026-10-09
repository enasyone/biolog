namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// BIO-ACC-001-AC02's own version workflow state: only ever moves forward, Draft -&gt; Published.
/// A version that is still Draft is revisable in place; once Published, its content is frozen -
/// any further change starts a new Draft version (see <see cref="AccessPolicy.ReviseDraft"/>),
/// never mutates a published one. Deliberately two states, not BIO-DOC-005's three
/// (Draft/Tested/Published) - AC02 asks only for "draft/publish", with no pre-publish testing
/// step of its own; AC04's "testable" refers to the evaluation engine being unit-testable, not a
/// workflow stage.
/// </summary>
public enum AccessPolicyStatus
{
    Draft,
    Published,
}

public static class AccessPolicyStatusExtensions
{
    public static string ToWireString(this AccessPolicyStatus status) => status switch
    {
        AccessPolicyStatus.Draft => "DRAFT",
        AccessPolicyStatus.Published => "PUBLISHED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}
