using Biolog.Features.PhotoQuality.Audit;

namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// One candidate photo staged for enrolment-staff review (BIO-PHO-003-AC01/AC02/AC03). A
/// mutable, CAS-versioned tenant aggregate under ADR-0001 §3.1/§3.2 - <see cref="Version"/> is a
/// real optimistic-concurrency token, starting at 1 and incrementing by exactly 1 on each
/// committed status transition (Pending -&gt; Rejected or Pending -&gt; Approved), the opposite
/// immutability shape from BIO-PHO-001's own <c>PhotoMaster</c> in the same epic - a capture is
/// a working record until decided, not a source-of-truth artifact from the moment it exists.
/// The identical shape BIO-PHO-002's own <c>BoothCapture</c> already established for a sibling
/// staging workflow.
///
/// <b>AC01/AC02: two independent quality signals, always both present.</b>
/// <see cref="PrintQualityPassed"/>/<see cref="PrintQualityReasons"/> and
/// <see cref="BiometricSuitabilityPassed"/>/<see cref="BiometricSuitabilityReasons"/> are
/// computed once, at submission, and never change afterward regardless of the operator's
/// eventual decision - either may independently be true or false (AC01's literal requirement),
/// and a failing verdict's reasons are always visible (AC02), not only when it is the reason a
/// capture was eventually rejected.
///
/// <b>No canonical id of its own kind.</b> Natural key: <c>(PersonId, SequenceNumber)</c> - the
/// same "no new registered type for a person-child aggregate" choice <c>PhotoMaster</c>
/// (BIO-PHO-001) and <c>BoothCapture</c> (BIO-PHO-002) made.
///
/// <b>Construction is internal (BIO-IDN-001-QA-01's lesson, applied here from the start).</b>
///
/// <b>Applying BIO-PHO-001-QA-01's lesson from the start.</b> <see cref="Content"/> uses the
/// identical "defensive copy in, defensive copy out" treatment <c>PhotoMaster</c>/<c>BoothCapture</c>
/// needed a review round to close.
///
/// <b>ZB-PHO003-02/03 (Zainab, security review): <see cref="PendingApprovalAttemptId"/>/
/// <see cref="PendingApprovalActorId"/>/<see cref="PendingApprovalActorType"/> are a durable,
/// resumable marker for an approval in progress - written BEFORE ever calling
/// <c>IPhotoMasterPromotionGateway.PromoteAsync</c>, not after.</b> Round 1's own marker was
/// written only AFTER promotion succeeded (a real crash window - round 2 fixed the ordering).
/// Round 2's own resumption check then identified "was this my promotion" by "latest version
/// beyond a recorded baseline, with matching content bytes" - round 3 found that provably wrong
/// two ways: an unrelated append sharing identical content could be falsely claimed, and a
/// LATER unrelated append could permanently bury this attempt's own earlier promotion (checking
/// only "latest" finds the wrong version, or none at all). These three fields instead record
/// INTENT - the attempt's own stable id (reused as both
/// <c>IPhotoMasterPromotionGateway.PromoteAsync</c>'s <c>correlationId</c> and the eventual
/// audit event's own id) and the IMMUTABLE initiating actor - durably, before the external call.
/// A retry (even under a DIFFERENT authorized actor) never infers identity from content or
/// position: it resolves "did this exact attempt already promote" via
/// <c>IPhotoMasterPromotionGateway.FindPromotedVersionAsync</c>, an EXACT lookup by this
/// attempt's own id at BIO-PHO-001's own promotion boundary. The original actor is always
/// credited on the eventual audit record, never whichever operator happens to complete the
/// retry. Valid only while <see cref="Status"/> is <see cref="PhotoQualityCaptureStatus.Pending"/>;
/// always null again once a capture reaches <see cref="PhotoQualityCaptureStatus.Approved"/> or
/// <see cref="PhotoQualityCaptureStatus.Rejected"/>.
/// </summary>
public sealed class PhotoQualityCapture
{
    private readonly byte[] _content;

    public string PersonId { get; }
    public string TenantId { get; }
    public int SequenceNumber { get; }
    public PhotoFormat Format { get; }
    public int WidthPx { get; }
    public int HeightPx { get; }
    public double? NativeDpi { get; }
    public byte[] Content => (byte[])_content.Clone();
    public bool PrintQualityPassed { get; }
    public IReadOnlyList<string> PrintQualityReasons { get; }
    public bool BiometricSuitabilityPassed { get; }
    public IReadOnlyList<string> BiometricSuitabilityReasons { get; }
    public PhotoQualityCaptureStatus Status { get; }
    public string? DecisionReason { get; }
    public int? PromotedMasterVersion { get; }
    public string? PendingApprovalAttemptId { get; }
    public string? PendingApprovalActorId { get; }
    public ActorType? PendingApprovalActorType { get; }
    public int Version { get; }
    public string SubmittedAt { get; }
    public string? DecidedAt { get; }

    internal PhotoQualityCapture(
        string personId, string tenantId, int sequenceNumber, PhotoFormat format, int widthPx, int heightPx, double? nativeDpi,
        byte[] content, bool printQualityPassed, IReadOnlyList<string> printQualityReasons, bool biometricSuitabilityPassed,
        IReadOnlyList<string> biometricSuitabilityReasons, PhotoQualityCaptureStatus status, string? decisionReason,
        int? promotedMasterVersion, int version, string submittedAt, string? decidedAt,
        string? pendingApprovalAttemptId = null, string? pendingApprovalActorId = null, ActorType? pendingApprovalActorType = null)
    {
        if (!Identifiers.CanonicalId.IsPersonId(personId))
        {
            throw new ArgumentException($"'{personId}' is not a well-formed person id (ADR-0001)", nameof(personId));
        }

        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), sequenceNumber, "a capture's sequence number starts at 1");
        }

        if (widthPx <= 0 || heightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPx), "width and height must both be positive");
        }

        if (content.Length == 0)
        {
            throw new ArgumentException("capture content must not be empty", nameof(content));
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "a capture's version starts at 1");
        }

        if (status == PhotoQualityCaptureStatus.Pending && decisionReason is not null)
        {
            throw new ArgumentException("a PENDING capture may not carry a decision reason", nameof(decisionReason));
        }

        if (status == PhotoQualityCaptureStatus.Rejected && string.IsNullOrWhiteSpace(decisionReason))
        {
            throw new ArgumentException("a REJECTED capture must carry a non-empty decision reason", nameof(decisionReason));
        }

        if (status == PhotoQualityCaptureStatus.Approved && promotedMasterVersion is null)
        {
            throw new ArgumentException("an APPROVED capture must carry the master version it was promoted to", nameof(promotedMasterVersion));
        }

        if (status != PhotoQualityCaptureStatus.Approved && promotedMasterVersion is not null)
        {
            throw new ArgumentException("only an APPROVED capture may carry a promoted master version", nameof(promotedMasterVersion));
        }

        var hasInFlightMarker = pendingApprovalAttemptId is not null;
        var markerFieldsConsistent = hasInFlightMarker == (pendingApprovalActorId is not null)
            && hasInFlightMarker == pendingApprovalActorType.HasValue;

        if (!markerFieldsConsistent)
        {
            throw new ArgumentException("an in-flight approval marker's attempt id, actor id and actor type must be set together, or not at all (ZB-PHO003-02)", nameof(pendingApprovalAttemptId));
        }

        if (status != PhotoQualityCaptureStatus.Pending && hasInFlightMarker)
        {
            throw new ArgumentException("an in-flight approval marker may only be carried by a PENDING capture (ZB-PHO003-02)", nameof(pendingApprovalAttemptId));
        }

        PersonId = personId;
        TenantId = tenantId;
        SequenceNumber = sequenceNumber;
        Format = format;
        WidthPx = widthPx;
        HeightPx = heightPx;
        NativeDpi = nativeDpi;
        _content = (byte[])content.Clone();
        PrintQualityPassed = printQualityPassed;
        PrintQualityReasons = printQualityReasons;
        BiometricSuitabilityPassed = biometricSuitabilityPassed;
        BiometricSuitabilityReasons = biometricSuitabilityReasons;
        Status = status;
        DecisionReason = decisionReason;
        PromotedMasterVersion = promotedMasterVersion;
        PendingApprovalAttemptId = pendingApprovalAttemptId;
        PendingApprovalActorId = pendingApprovalActorId;
        PendingApprovalActorType = pendingApprovalActorType;
        Version = version;
        SubmittedAt = submittedAt;
        DecidedAt = decidedAt;
    }
}
