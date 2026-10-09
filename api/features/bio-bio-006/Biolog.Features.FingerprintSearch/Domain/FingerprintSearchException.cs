namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>Domain-level error codes this feature raises. Plain strings, not an HTTP status -
/// this project has no ASP.NET dependency, the same deferred boundary every prior BIO-BIO story
/// documents.</summary>
public sealed class FingerprintSearchException : InvalidOperationException
{
    public string Code { get; }

    private FingerprintSearchException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>AC02: the calling actor does not hold the 1:N search permission. A genuine
    /// authorization failure, not a routine search outcome.</summary>
    public static FingerprintSearchException PermissionRequired(string permissionKey) =>
        new("fingerprint_search_permission_required", $"actor does not hold the required permission '{permissionKey}' for 1:N fingerprint search.");

    /// <summary>AC03: a review decision was attempted for a pair with no raised entry.</summary>
    public static FingerprintSearchException ReviewEntryNotFound(string personIdA, string personIdB) =>
        new("fingerprint_duplicate_review_entry_not_found", $"no duplicate review entry exists for the pair '{personIdA}'/'{personIdB}'.");

    public static FingerprintSearchException ConcurrencyConflict(string personIdA, string personIdB, int expectedVersion, int actualVersion) =>
        new("concurrency_conflict",
            $"pair '{personIdA}'/'{personIdB}' expected version {expectedVersion} but found {actualVersion}.");
}
