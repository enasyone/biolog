using System.Text.RegularExpressions;

namespace Biolog.Features.DocumentVerifyCoordination;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1) this coordinator mints. <c>dcs_</c> is
/// already a registered prefix in <c>contracts/schemas/identifier-registry.json</c>
/// ("One Document Verify session and the evidence captured within it.") - BIO-DOC-001's own
/// <see cref="Biolog.Features.DocumentVerify.Model.DocumentSession"/> accepts a caller-supplied id
/// but validates no shape itself (see that type's own remarks); this is the mint this coordinator
/// uses to supply a well-formed one, the same per-feature local copy every sibling in this epic
/// keeps rather than a shared/common identifier module. <c>evt_</c> is minted/validated the same
/// way for correlation ids (ADR-0004 Decision 3.2), mirroring every sibling feature's own local
/// <c>CorrelationIdHeader</c>.
/// </summary>
public static partial class CanonicalId
{
    public const string DocumentSessionPrefix = "dcs";
    public const string EventPrefix = "evt";

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    public static string NewDocumentSessionId() => $"{DocumentSessionPrefix}_{Guid.CreateVersion7():D}";

    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);
}
