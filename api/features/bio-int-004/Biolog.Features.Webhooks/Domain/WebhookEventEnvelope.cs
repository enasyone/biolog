using System.Text.Json.Serialization;

namespace Biolog.Features.Webhooks;

/// <summary>Mirrors <c>Biolog.Features.Notifications.AggregateType</c>'s own five-value
/// enum, itself constrained by the event-envelope schema's `aggregate_type` field
/// (`contracts/schemas/event-envelope.schema.json`) - a third independent copy, not a
/// reference (this project cannot reference bio-rpt-001 any more than it can reference
/// Biolog.Api - see this project's own .csproj comment).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AggregateType>))]
public enum AggregateType
{
    Tenant,
    Person,
    Credential,
    Device,
    DocumentSession,
}

/// <summary>Mirrors the schema's `payload_classification` vocabulary exactly as
/// BIO-RPT-001's own <c>EventClassification</c> does.</summary>
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
/// (`contracts/schemas/event-envelope.schema.json`), field-for-field identical to
/// BIO-RPT-001's own <c>DomainEventEnvelope</c> - a THIRD independent duplicate of the same
/// schema, not a refactor of either existing copy (this project can reference neither
/// sibling feature project). <c>api/Biolog.Api/Modules/BioInt004Module.cs</c> is the one
/// place that translates a real <c>Biolog.Features.Notifications.DomainEventEnvelope</c>
/// (subscribed from the real, shared <c>IDomainEventBus</c>) into this project's own shape,
/// field by field - the same host-module translation-boundary pattern
/// <c>BioDev003Module</c>'s own <c>CertificationGateAdapter</c> already established for an
/// analogous cross-feature-type boundary.
/// </summary>
public sealed record WebhookEventEnvelope(
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
