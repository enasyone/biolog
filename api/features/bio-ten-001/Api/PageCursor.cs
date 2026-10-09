using Biolog.Features.TenantRegistry.Identifiers;

namespace Biolog.Features.TenantRegistry.Api;

/// <summary>
/// ADR-0004 Decision 5.1: the list cursor is opaque - a client echoes it back verbatim and
/// never constructs or parses it. This feature's cursor happens to encode the last-seen
/// tenant id, but that is this implementation's private detail: it is base64-wrapped
/// specifically so nothing about "it is a tenant id" is a promise to callers, and a future
/// change to the encoding is not a breaking change to the wire contract.
/// </summary>
public static class PageCursor
{
    public static string Encode(string lastId) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(lastId));

    /// <summary>Returns false for anything that does not decode to a well-formed tenant id - a malformed cursor is a validation error, not a silent reset to page one.</summary>
    public static bool TryDecode(string cursor, out string tenantId)
    {
        tenantId = "";
        try
        {
            var bytes = Convert.FromBase64String(cursor);
            var decoded = System.Text.Encoding.UTF8.GetString(bytes);
            if (!CanonicalId.IsTenantId(decoded))
            {
                return false;
            }

            tenantId = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
