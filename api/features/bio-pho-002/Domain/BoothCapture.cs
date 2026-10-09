namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// One photo-booth submission staged for review (BIO-PHO-002-AC03/AC04). A mutable, CAS-versioned
/// tenant aggregate under ADR-0001 §3.1/§3.2 - <see cref="Version"/> is a real optimistic-
/// concurrency token, starting at 1 and incrementing by exactly 1 on each committed status
/// transition (Pending -&gt; Rejected or Pending -&gt; Promoted), the opposite immutability shape
/// from BIO-PHO-001's own <c>PhotoMaster</c> in the same epic - a capture is a working record
/// until decided, not a source-of-truth artifact from the moment it exists.
///
/// <b>No canonical id of its own kind.</b> Natural key: <c>(PersonId, SequenceNumber)</c> - the
/// same "no new registered type for a person-child aggregate" choice <c>PhotoMaster</c>
/// (BIO-PHO-001) and <c>PersonProfile</c> (BIO-IDN-002) made, for an identical reason.
///
/// <b>Construction is internal (BIO-IDN-001-QA-01's lesson, applied here from the start).</b>
///
/// <b>Applying BIO-PHO-001-QA-01's lesson from the start.</b> <see cref="Content"/> uses the
/// identical "defensive copy in, defensive copy out" treatment <c>PhotoMaster</c>/<c>PhotoDerivative</c>
/// needed a review round to close - a get-only <c>byte[]</c> alone hands out a live, mutable
/// reference; the constructor clones the caller's array before storing it, and the property
/// clones the stored array on every read, applied here before any reviewer had to find it twice.
/// </summary>
public sealed class BoothCapture
{
    private readonly byte[] _content;

    public string PersonId { get; }
    public string TenantId { get; }
    public string DeviceId { get; }
    public int SequenceNumber { get; }
    public CaptureFormat Format { get; }
    public int WidthPx { get; }
    public int HeightPx { get; }
    public double? NativeDpi { get; }
    public bool FaceHeightCompliant { get; }
    public bool BackgroundCompliant { get; }
    public byte[] Content => (byte[])_content.Clone();
    public CaptureStatus Status { get; }
    public IReadOnlyList<string> RejectionReasons { get; }
    public int? PromotedMasterVersion { get; }
    public int Version { get; }
    public string SubmittedAt { get; }
    public string? DecidedAt { get; }

    internal BoothCapture(
        string personId, string tenantId, string deviceId, int sequenceNumber, CaptureFormat format, int widthPx, int heightPx,
        double? nativeDpi, bool faceHeightCompliant, bool backgroundCompliant, byte[] content, CaptureStatus status,
        IReadOnlyList<string> rejectionReasons, int? promotedMasterVersion, int version, string submittedAt, string? decidedAt)
    {
        if (!Identifiers.CanonicalId.IsPersonId(personId))
        {
            throw new ArgumentException($"'{personId}' is not a well-formed person id (ADR-0001)", nameof(personId));
        }

        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (!Identifiers.CanonicalId.IsDeviceId(deviceId))
        {
            throw new ArgumentException($"'{deviceId}' is not a well-formed device id (ADR-0001)", nameof(deviceId));
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

        if (status == CaptureStatus.Pending && rejectionReasons.Count > 0)
        {
            throw new ArgumentException("a PENDING capture may not carry rejection reasons", nameof(rejectionReasons));
        }

        if (status == CaptureStatus.Rejected && rejectionReasons.Count == 0)
        {
            throw new ArgumentException("a REJECTED capture must carry at least one rejection reason", nameof(rejectionReasons));
        }

        if (status == CaptureStatus.Promoted && promotedMasterVersion is null)
        {
            throw new ArgumentException("a PROMOTED capture must carry the master version it was promoted to", nameof(promotedMasterVersion));
        }

        if (status != CaptureStatus.Promoted && promotedMasterVersion is not null)
        {
            throw new ArgumentException("only a PROMOTED capture may carry a promoted master version", nameof(promotedMasterVersion));
        }

        PersonId = personId;
        TenantId = tenantId;
        DeviceId = deviceId;
        SequenceNumber = sequenceNumber;
        Format = format;
        WidthPx = widthPx;
        HeightPx = heightPx;
        NativeDpi = nativeDpi;
        FaceHeightCompliant = faceHeightCompliant;
        BackgroundCompliant = backgroundCompliant;
        _content = (byte[])content.Clone();
        Status = status;
        RejectionReasons = rejectionReasons;
        PromotedMasterVersion = promotedMasterVersion;
        Version = version;
        SubmittedAt = submittedAt;
        DecidedAt = decidedAt;
    }
}
