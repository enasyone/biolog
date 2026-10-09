using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.Attendance.Identifiers;

namespace Biolog.Features.Attendance.Domain;

/// <summary>
/// One immutable, append-only attendance fact (AC01, AC02). Natural key
/// <c>(TenantId, IdempotencyKey)</c> - no update, no version field, ever (the same "an event is
/// a fact and a version implies it can be updated" discipline ADR-0003 Decision 2 states for the
/// shared event envelope, applied to this feature's own domain-specific fact record - this type
/// is a right-sized local record, not a literal instance of the shared event-envelope schema,
/// the same "hand-ported pattern, not a shared library" precedent
/// <c>Audit/AttendanceAuditEvent.cs</c> documents for the audit envelope specifically).
///
/// <b>AC02 - raw is never derived.</b> This record carries only what was physically observed
/// (who, source/device, where, when, how it was verified) - it has no "status" field of any
/// kind. <see cref="AttendanceStatusEvaluator"/> computes present/late/absent from a set of
/// these events plus the tenant's current <see cref="AttendancePolicy"/> at READ time; nothing
/// here is ever mutated to reflect that computation, mirroring BIO-IDN-002's "refuse auto-flip
/// for time-based status - compute at read time" discipline for an identical reason (no
/// background scheduler infrastructure exists to keep a stored derived value from going stale).
///
/// <b>AC03 - idempotent capture.</b> <see cref="IdempotencyKey"/> is producer-minted (the
/// device/client mints and remembers it, e.g. per local sequence number) - the same
/// "redelivery of the identical event_id must not create a duplicate fact" mechanism ADR-0003
/// Decision 4 establishes for the shared audit/event envelope, ported into this feature's own
/// natural key rather than reusing <c>evt_</c> identity for it (an <c>evt_</c> id here names
/// THIS specific captured fact once accepted, not the producer's own retry correlation value -
/// keeping them distinct means a legitimate retry with the SAME idempotency key can be recognised
/// and answered idempotently before a new <c>evt_</c> id is ever minted). <see cref="ContentFingerprint"/>
/// is a hash of every business field except <see cref="EventId"/>/<see cref="ReceivedAt"/> (the
/// two fields a server assigns per delivery, never per content) - a caller reusing an
/// idempotency key with DIFFERENT content is a conflict (<see cref="AttendanceEventService.CaptureAsync"/>),
/// never a silently-dropped or silently-overwritten event, the identical "second delivery with
/// different content must never be absorbed" rule ADR-0003 Decision 4 establishes for the shared
/// envelope's own dedupe.
///
/// <b>Disclosed scope boundary.</b> This mechanism defends against REDELIVERY of the same
/// logical capture (a device retrying its own already-sent tap). It does not attempt to detect
/// two events with two DIFFERENT idempotency keys that describe the same physical occurrence
/// (e.g. two independent devices both reporting one tap) - that is a device/deployment-topology
/// concern outside a single capture endpoint's own reach, the same "disclosed, not silently
/// absorbed" boundary BIO-ACC-001 recorded for caller-supplied evaluation inputs it does not
/// resolve.
/// </summary>
public sealed record AttendanceEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required AttendanceSource Source { get; init; }

    /// <summary>Required if and only if <see cref="Source"/> is <see cref="AttendanceSource.Device"/> - shape-validated only, never existence-gated (BIO-DEV-001 is not a declared dependency of this story).</summary>
    public string? DeviceId { get; init; }

    /// <summary>Free-form site/location label. Nullable - not every capture path names one (e.g. a manual entry).</summary>
    public string? Location { get; init; }

    public required AttendanceMode Mode { get; init; }
    public required VerificationEvidence Evidence { get; init; }

    /// <summary>When the event actually happened, per the producer's clock (RFC 3339 UTC).</summary>
    public required string OccurredAt { get; init; }

    /// <summary>When this server accepted it - a server-stamped value, never caller-supplied, and excluded from <see cref="ContentFingerprint"/> for the identical reason ADR-0003 Decision 4 excludes it from its own dedupe fingerprint.</summary>
    public required string ReceivedAt { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string ContentFingerprint { get; init; }

    /// <summary>The only sanctioned way to construct one - validates every AC01 field and computes <see cref="ContentFingerprint"/> itself so a caller can never mint an event whose fingerprint does not actually describe its own content.</summary>
    public static AttendanceEvent Capture(
        string tenantId, string personId, AttendanceSource source, string? deviceId, string? location,
        AttendanceMode mode, VerificationEvidence evidence, string occurredAt, string receivedAt, string idempotencyKey)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id", nameof(tenantId));
        }

        if (!CanonicalId.IsPersonId(personId))
        {
            throw AttendanceError.Validation([new Api.ValidationErrorItem("person_id", "invalid_format", "person_id must be a canonical per_ identifier")]);
        }

        if (source == AttendanceSource.Device && string.IsNullOrWhiteSpace(deviceId))
        {
            throw AttendanceError.Validation([new Api.ValidationErrorItem("device_id", "required", "device_id is required when source is 'device'")]);
        }

        if (deviceId is not null && !CanonicalId.IsDeviceId(deviceId))
        {
            throw AttendanceError.Validation([new Api.ValidationErrorItem("device_id", "invalid_format", "device_id must be a canonical dev_ identifier")]);
        }

        if (source != AttendanceSource.Device && deviceId is not null)
        {
            throw AttendanceError.Validation([new Api.ValidationErrorItem("device_id", "not_allowed", "device_id may only be present when source is 'device'")]);
        }

        if (!UtcInstant.TryParse(occurredAt, out _))
        {
            throw AttendanceError.Validation([new Api.ValidationErrorItem("occurred_at", "invalid_format", "occurred_at must be an RFC 3339 UTC instant")]);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw AttendanceError.Validation([new Api.ValidationErrorItem("idempotency_key", "invalid", "idempotency_key must be non-empty and at most 200 characters")]);
        }

        var validEvidence = VerificationEvidence.Validate(evidence);

        var fingerprint = ComputeFingerprint(tenantId, personId, source, deviceId, location, mode, validEvidence, occurredAt, idempotencyKey);

        return new AttendanceEvent
        {
            EventId = CanonicalId.NewEventId(),
            TenantId = tenantId,
            PersonId = personId,
            Source = source,
            DeviceId = deviceId,
            Location = location,
            Mode = mode,
            Evidence = validEvidence,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt,
            IdempotencyKey = idempotencyKey,
            ContentFingerprint = fingerprint,
        };
    }

    /// <summary>
    /// SHA-256 over a canonical JSON projection of every field a caller controls, EXCLUDING
    /// <see cref="EventId"/> and <see cref="ReceivedAt"/> (both server-assigned per delivery,
    /// never per content) - the identical exclusion ADR-0003 Decision 4 makes for its own
    /// redelivery fingerprint, and for the identical reason: including a per-delivery timestamp
    /// would make every legitimate retry look like new content and defeat the dedupe entirely.
    /// </summary>
    public static string ComputeFingerprint(
        string tenantId, string personId, AttendanceSource source, string? deviceId, string? location,
        AttendanceMode mode, VerificationEvidence evidence, string occurredAt, string idempotencyKey)
    {
        var projection = new
        {
            tenant_id = tenantId,
            person_id = personId,
            source = source.ToWireString(),
            device_id = deviceId,
            location,
            mode = mode.ToWireString(),
            evidence = new { method = evidence.Method.ToWireString(), verified = evidence.Verified, confidence = evidence.Confidence },
            occurred_at = occurredAt,
            idempotency_key = idempotencyKey,
        };
        var canonicalJson = JsonSerializer.Serialize(projection);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return Convert.ToHexStringLower(hash);
    }
}
