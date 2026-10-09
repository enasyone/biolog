namespace Biolog.Features.EvidenceComparison.Model;

/// <summary>In-process, per-session version sequence tracker for BIO-DOC-008 - the same in-memory
/// seam every audited BIOLOG feature's own store (<c>InMemoryTenantAuditSink</c> etc.) ships with;
/// a real durable, cross-process store is a later story's job.</summary>
public sealed class InMemoryVersionStore : IVersionStore
{
    private readonly Dictionary<string, int> _committedVersionBySession = [];
    private readonly HashSet<string> _sessionsWithOutstandingReservation = [];
    private readonly object _gate = new();

    public bool AllocateVersion(string documentSessionId, int versionNumber)
    {
        lock (_gate)
        {
            if (_sessionsWithOutstandingReservation.Contains(documentSessionId))
            {
                return false; // another reservation for this session has not yet resolved
            }

            var committedVersion = _committedVersionBySession.GetValueOrDefault(documentSessionId, 0);
            if (versionNumber != committedVersion + 1)
            {
                return false; // duplicate, skipped, or backward - reject, never silently accept
            }

            _sessionsWithOutstandingReservation.Add(documentSessionId);
            return true;
        }
    }

    public void CommitVersion(string documentSessionId, int versionNumber)
    {
        lock (_gate)
        {
            _committedVersionBySession[documentSessionId] = versionNumber;
            _sessionsWithOutstandingReservation.Remove(documentSessionId);
        }
    }

    public void ReleaseVersion(string documentSessionId, int versionNumber)
    {
        lock (_gate)
        {
            // The committed sequence was never advanced by AllocateVersion, so releasing only
            // needs to clear the outstanding-reservation marker - versionNumber remains reissuable.
            _sessionsWithOutstandingReservation.Remove(documentSessionId);
        }
    }
}
