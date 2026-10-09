using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Biolog.Features.TransportEvents.Identifiers;

namespace Biolog.Features.TransportEvents.Domain;

/// <summary>
/// One immutable, append-only board/alight fact (AC01, AC02). Natural key
/// <c>(TenantId, IdempotencyKey)</c> - no update, no version field, ever, the same "an event is a
/// fact" discipline BIO-ATT-001's own <c>AttendanceEvent</c> applies, mirrored here field-for-field
/// with this story's own shape.
///
/// <b>AC03 - never carries a status.</b> This record carries only what was physically observed
/// (who, route, vehicle, stop/location, when, how identity was verified) - <see cref="TransportPresenceEvaluator"/>
/// computes the derived on-board/alighted/exception classification from a set of these at READ
/// time; nothing here is ever mutated to reflect that computation.
///
/// <b>AC02 - idempotent capture, identical mechanism to BIO-ATT-001's own AC03.</b>
/// <see cref="IdempotencyKey"/> is producer-minted; <see cref="ContentFingerprint"/> is a hash of
/// every business field except <see cref="EventId"/>/<see cref="ReceivedAt"/> (both server-
/// assigned per delivery, never per content). A caller reusing an idempotency key with DIFFERENT
/// content is a conflict (<see cref="TransportEventService.CaptureAsync"/>), never a silently
/// dropped or silently overwritten event.
/// </summary>
public sealed record TransportEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required string RouteId { get; init; }
    public required string VehicleId { get; init; }

    /// <summary>Nullable - existence/route-membership checked by <see cref="TransportEventService"/> when present. Not every capture path resolves a formal stop.</summary>
    public string? StopId { get; init; }

    /// <summary>Free-form location label, used when no formal <see cref="StopId"/> applies - mirrors BIO-ATT-001's own free-text <c>Location</c> field precedent (never existence-gated).</summary>
    public string? Location { get; init; }

    public required TransportEventMode Mode { get; init; }

    /// <summary>At least one entry required - AC01's "configurable UHF/NFC/face combinations" (see <see cref="EvidenceMethod"/>'s own header for why this is a list).</summary>
    public required IReadOnlyList<TransportEvidenceEntry> Evidence { get; init; }

    /// <summary>When the event actually happened, per the producer's clock (RFC 3339 UTC).</summary>
    public required string OccurredAt { get; init; }

    /// <summary>When this server accepted it - server-stamped, never caller-supplied, excluded from <see cref="ContentFingerprint"/>.</summary>
    public required string ReceivedAt { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string ContentFingerprint { get; init; }

    /// <summary>The only sanctioned way to construct one - validates every AC01 field and computes <see cref="ContentFingerprint"/> itself so a caller can never mint an event whose fingerprint does not actually describe its own content.</summary>
    public static TransportEvent Capture(
        string tenantId, string personId, string routeId, string vehicleId, string? stopId, string? location,
        TransportEventMode mode, IReadOnlyList<TransportEvidenceEntry> evidence, string occurredAt, string receivedAt, string idempotencyKey)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (!CanonicalId.IsPersonId(personId))
        {
            throw TransportEventError.Validation([new Api.ValidationErrorItem("person_id", "invalid_format", "person_id must be a canonical per_ identifier")]);
        }

        if (string.IsNullOrWhiteSpace(routeId))
        {
            throw TransportEventError.Validation([new Api.ValidationErrorItem("route_id", "required", "route_id is required")]);
        }

        if (string.IsNullOrWhiteSpace(vehicleId))
        {
            throw TransportEventError.Validation([new Api.ValidationErrorItem("vehicle_id", "required", "vehicle_id is required")]);
        }

        if (!UtcInstant.TryParse(occurredAt, out _))
        {
            throw TransportEventError.Validation([new Api.ValidationErrorItem("occurred_at", "invalid_format", "occurred_at must be an RFC 3339 UTC instant")]);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
        {
            throw TransportEventError.Validation([new Api.ValidationErrorItem("idempotency_key", "invalid", "idempotency_key must be non-empty and at most 200 characters")]);
        }

        if (evidence.Count == 0)
        {
            throw TransportEventError.Validation([new Api.ValidationErrorItem("evidence", "required", "at least one evidence entry is required")]);
        }

        var validEvidence = evidence.Select((e, i) => TransportEvidenceEntry.Validate(e, i)).ToList();

        var fingerprint = ComputeFingerprint(tenantId, personId, routeId, vehicleId, stopId, location, mode, validEvidence, occurredAt, idempotencyKey);

        return new TransportEvent
        {
            EventId = CanonicalId.NewEventId(),
            TenantId = tenantId,
            PersonId = personId,
            RouteId = routeId,
            VehicleId = vehicleId,
            StopId = stopId,
            Location = location,
            Mode = mode,
            Evidence = validEvidence,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt,
            IdempotencyKey = idempotencyKey,
            ContentFingerprint = fingerprint,
        };
    }

    /// <summary>SHA-256 over a canonical JSON projection of every field a caller controls, EXCLUDING <see cref="EventId"/> and <see cref="ReceivedAt"/> - the identical exclusion BIO-ATT-001's own fingerprint makes, for the identical reason (a per-delivery timestamp would make every legitimate retry look like new content).</summary>
    public static string ComputeFingerprint(
        string tenantId, string personId, string routeId, string vehicleId, string? stopId, string? location,
        TransportEventMode mode, IReadOnlyList<TransportEvidenceEntry> evidence, string occurredAt, string idempotencyKey)
    {
        var projection = new
        {
            tenant_id = tenantId,
            person_id = personId,
            route_id = routeId,
            vehicle_id = vehicleId,
            stop_id = stopId,
            location,
            mode = mode.ToWireString(),
            evidence = evidence.Select(e => new { method = e.Method.ToWireString(), verified = e.Verified, confidence = e.Confidence }).ToArray(),
            occurred_at = occurredAt,
            idempotency_key = idempotencyKey,
        };
        var canonicalJson = JsonSerializer.Serialize(projection);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return Convert.ToHexStringLower(hash);
    }
}
