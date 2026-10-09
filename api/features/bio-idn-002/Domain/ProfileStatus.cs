namespace Biolog.Features.PersonProfileRegistry.Domain;

/// <summary>
/// BIO-IDN-002-AC03. Deliberately just two states, one-way: a profile starts
/// <see cref="Active"/> at assignment (no separate "pending" state - no AC asks for deferred
/// activation) and can only be administratively <see cref="Revoked"/> (terminal) - the
/// profile's own "governed deletion", mirroring BIO-IDN-001's PERSON ARCHIVED ethos at a
/// smaller scale: revoking a profile never deletes the record, so operational history that
/// referenced it (e.g. attendance events recorded while the profile was active) is never
/// orphaned. Natural, TIME-based expiry (<see cref="PersonProfile.ExpiresAt"/> passing) is
/// deliberately NOT a third stored state this field transitions into automatically - no
/// background scheduler exists anywhere in this codebase to flip it, and computing
/// "in effect right now" from <see cref="PersonProfile.Status"/> plus
/// <see cref="PersonProfile.ExpiresAt"/> at read time (see
/// <see cref="PersonProfile.IsCurrentlyInEffect"/>) is simpler and cannot drift out of sync
/// with the clock the way a stored, mutated field could.
/// </summary>
public enum ProfileStatus
{
    Active,
    Revoked,
}

public static class ProfileStatusExtensions
{
    public static string ToWireString(this ProfileStatus status) => status switch
    {
        ProfileStatus.Active => "ACTIVE",
        ProfileStatus.Revoked => "REVOKED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out ProfileStatus status)
    {
        switch (value)
        {
            case "ACTIVE": status = ProfileStatus.Active; return true;
            case "REVOKED": status = ProfileStatus.Revoked; return true;
            default: status = default; return false;
        }
    }
}
