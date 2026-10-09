using System.Text.Json.Serialization;

namespace Biolog.Features.Notifications;

/// <summary>The six ADR-0001 §1.2 aggregate types the domain-event envelope schema
/// (`contracts/schemas/event-envelope.schema.json`) constrains `aggregate_type` to, minus
/// the ones with no canonical id prefix registered yet - this project mirrors the schema's
/// exact five-value enum (`tenant`, `person`, `credential`, `device`,
/// `document_session`), never invents a sixth (that would need a schema change and Kemi's
/// approval, outside this story's scope).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AggregateType>))]
public enum AggregateType
{
    Tenant,
    Person,
    Credential,
    Device,
    DocumentSession,
}

/// <summary>The event envelope schema's `payload_classification` vocabulary (ADR-0003
/// Decision 3's five levels, minus `secret` - a domain event's OWN payload_classification
/// field is schema-constrained to these four; `secret` material is never carried in a
/// domain event's payload at all, so there is no fifth value to represent here).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EventClassification>))]
public enum EventClassification
{
    Public,
    Internal,
    Personal,
    Sensitive,
}

/// <summary>
/// This project's own copy of the BIO-ARC-003 domain-event envelope
/// (`contracts/schemas/event-envelope.schema.json`) - field-for-field, in the same field
/// order the schema lists `required` - kept as a plain record here because this project
/// cannot reference anything inside `Biolog.Api` (see the .csproj's own comment), so it
/// cannot share a type with any future story that publishes a REAL domain event from inside
/// the host. A future integration point translates between the two the same way
/// `BioDev003Module`'s `CertificationGateAdapter` already translates between two
/// independently-shaped device types across the same kind of boundary.
///
/// <see cref="EventId"/>/<see cref="AggregateId"/>/<see cref="CorrelationId"/> are plain
/// strings, not this project's own canonical-id type: this story has nothing to validate
/// their shape for beyond what the schema itself already governs, and re-deriving
/// <c>Biolog.Api.Identifiers.CanonicalId</c>'s validation here would duplicate logic this
/// project has no way to keep in sync with the real one.
/// </summary>
public sealed record DomainEventEnvelope(
    string EventId,
    int SchemaVersion,
    string TenantId,
    AggregateType AggregateType,
    string AggregateId,
    int AggregateVersion,
    string CorrelationId,
    string? CausationId,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    string Producer,
    EventClassification PayloadClassification,
    IReadOnlyDictionary<string, string> Payload,
    string? IdempotencyKey = null);
