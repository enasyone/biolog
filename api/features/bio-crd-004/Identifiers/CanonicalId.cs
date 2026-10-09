using System.Text.RegularExpressions;

namespace Biolog.Features.CardObjects.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the one type this feature
/// touches: <c>ten_</c> (defence-in-depth shape validation for the resolved tenant, mirroring
/// BIO-IDN-001-QA-01's identical use in <c>Person</c>'s own constructor, and every sibling
/// story's identical local copy). This is a local, schema-conformant helper - not the shared
/// contract; no generated .NET binding exists yet. This project registers no new identifier
/// type of its own - <see cref="Domain.SignatureAsset"/> deliberately has no canonical id of
/// its own kind (it is keyed by a natural/composite key against a caller-supplied lineage id -
/// see that type's header for why).
/// </summary>
public static partial class CanonicalId
{
    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);
}
