using System.Collections.Immutable;

namespace Biolog.Features.FingerprintEnrolment.Domain;

/// <summary>
/// BIO-BIO-002: the guided tenprint/single-finger enrolment aggregate. Every mutation is
/// optimistic-concurrency protected (ADR-0001 Decision 3: <see cref="Version"/> starts at 1,
/// every mutation states the version it believes it is changing, bumps by exactly 1) and
/// returns a NEW session instance rather than mutating in place - the same "validated at the
/// aggregate boundary, re-validated at the persistence boundary" split
/// <c>PersonRepositoryGovernanceTests</c> established, applied here via
/// <see cref="Domain.IFingerprintEnrolmentRepository"/>.
///
/// <b>AC01.</b> <see cref="Mode"/> fixes <see cref="TargetPositions"/> at <see cref="Start"/>:
/// exactly the ten standard positions for <see cref="EnrolmentMode.TenPrint"/>, exactly one
/// caller-chosen position for <see cref="EnrolmentMode.SingleFinger"/>. A capture/exception for
/// a position outside <see cref="TargetPositions"/> is rejected - a session cannot silently grow
/// beyond what its mode declared.
///
/// <b>AC03.</b> Recapture is simply calling <see cref="CaptureFinger"/> again for an already-
/// <see cref="FingerCaptureStatus.Captured"/> position (it supersedes the prior record, see
/// <see cref="FingerRecord"/>'s remarks); <see cref="MarkMissingOrAmputated"/> is the distinct,
/// terminal exception path for a position that cannot ever be captured. Neither can be applied to
/// a position the other has already resolved to <see cref="FingerCaptureStatus.MissingAmputated"/>
/// without an explicit correction path this story does not define - see
/// <see cref="FingerprintEnrolmentException.SessionNotInProgress"/>'s sibling errors.
/// </summary>
public sealed record FingerprintEnrolmentSession
{
    public string Id { get; }
    public string TenantId { get; }
    public string PersonId { get; }
    public EnrolmentMode Mode { get; }
    public ImmutableArray<FingerPosition> TargetPositions { get; }
    public bool RetentionAuthorised { get; }
    public EnrolmentSessionStatus Status { get; private init; }
    public ImmutableDictionary<FingerPosition, FingerRecord> Fingers { get; private init; }
    public int Version { get; private init; }
    public string CreatedAtUtc { get; }
    public string UpdatedAtUtc { get; private init; }

    private FingerprintEnrolmentSession(
        string id, string tenantId, string personId, EnrolmentMode mode, ImmutableArray<FingerPosition> targetPositions,
        bool retentionAuthorised, EnrolmentSessionStatus status, ImmutableDictionary<FingerPosition, FingerRecord> fingers,
        int version, string createdAtUtc, string updatedAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        PersonId = personId;
        Mode = mode;
        TargetPositions = targetPositions;
        RetentionAuthorised = retentionAuthorised;
        Status = status;
        Fingers = fingers;
        Version = version;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public static FingerprintEnrolmentSession Start(
        string id, string tenantId, string personId, EnrolmentMode mode, IReadOnlyList<FingerPosition> requestedPositions,
        bool retentionAuthorised, string nowUtc)
    {
        RequireNonEmpty(id, nameof(id));
        RequireNonEmpty(tenantId, nameof(tenantId));
        RequireNonEmpty(personId, nameof(personId));
        RequireNonEmpty(nowUtc, nameof(nowUtc));

        ImmutableArray<FingerPosition> targets;
        if (mode == EnrolmentMode.TenPrint)
        {
            targets = [.. FingerPositionExtensions.AllTenPrintPositions];
        }
        else
        {
            if (requestedPositions is not { Count: 1 })
            {
                throw FingerprintEnrolmentException.SingleFingerPositionRequired();
            }
            targets = [.. requestedPositions];
        }

        var fingers = targets.ToImmutableDictionary(p => p, FingerRecord.CreatePending);
        return new FingerprintEnrolmentSession(
            id, tenantId, personId, mode, targets, retentionAuthorised, EnrolmentSessionStatus.InProgress, fingers, 1, nowUtc, nowUtc);
    }

    public FingerprintEnrolmentSession CaptureFinger(FingerPosition position, FingerRecord capturedRecord, int expectedVersion, string nowUtc)
    {
        RequireInProgress();
        RequireTargetPosition(position);
        CheckVersion(expectedVersion);

        var current = Fingers[position];
        if (current.Status == FingerCaptureStatus.MissingAmputated)
        {
            throw FingerprintEnrolmentException.PositionAlreadyMissingAmputated(position);
        }

        var updated = capturedRecord with
        {
            Position = position,
            Status = FingerCaptureStatus.Captured,
            CaptureCount = current.CaptureCount + 1,
            UpdatedAtUtc = nowUtc,
        };

        return this with { Fingers = Fingers.SetItem(position, updated), Version = Version + 1, UpdatedAtUtc = nowUtc };
    }

    public FingerprintEnrolmentSession MarkMissingOrAmputated(FingerPosition position, string reason, int expectedVersion, string nowUtc)
    {
        RequireNonEmpty(reason, nameof(reason));
        RequireInProgress();
        RequireTargetPosition(position);
        CheckVersion(expectedVersion);

        var current = Fingers[position];
        if (current.Status == FingerCaptureStatus.MissingAmputated)
        {
            return this; // idempotent: already resolved the same way
        }

        var updated = new FingerRecord
        {
            Position = position,
            Status = FingerCaptureStatus.MissingAmputated,
            MissingAmputatedReason = reason,
            CaptureCount = current.CaptureCount,
            UpdatedAtUtc = nowUtc,
        };

        return this with { Fingers = Fingers.SetItem(position, updated), Version = Version + 1, UpdatedAtUtc = nowUtc };
    }

    public FingerprintEnrolmentSession Complete(int expectedVersion, string nowUtc)
    {
        RequireInProgress();
        CheckVersion(expectedVersion);

        var unresolved = TargetPositions.Where(p => Fingers[p].Status == FingerCaptureStatus.Pending).ToList();
        if (unresolved.Count > 0)
        {
            throw FingerprintEnrolmentException.IncompleteEnrolment(unresolved);
        }

        return this with { Status = EnrolmentSessionStatus.Completed, Version = Version + 1, UpdatedAtUtc = nowUtc };
    }

    public FingerprintEnrolmentSession Cancel(int expectedVersion, string nowUtc)
    {
        RequireInProgress();
        CheckVersion(expectedVersion);
        return this with { Status = EnrolmentSessionStatus.Cancelled, Version = Version + 1, UpdatedAtUtc = nowUtc };
    }

    private void RequireInProgress()
    {
        if (Status != EnrolmentSessionStatus.InProgress)
        {
            throw FingerprintEnrolmentException.SessionNotInProgress(Id, Status);
        }
    }

    private void RequireTargetPosition(FingerPosition position)
    {
        if (!Fingers.ContainsKey(position))
        {
            throw FingerprintEnrolmentException.PositionNotTargetedByMode(position, Mode);
        }
    }

    private void CheckVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw FingerprintEnrolmentException.ConcurrencyConflict(Id, expectedVersion, Version);
        }
    }

    private static void RequireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("must be a non-empty string", paramName);
        }
    }
}
