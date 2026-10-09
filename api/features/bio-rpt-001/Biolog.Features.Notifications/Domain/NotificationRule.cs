namespace Biolog.Features.Notifications;

/// <summary>
/// A tenant's own configuration: "when a <see cref="AggregateType"/> event in the
/// <see cref="MinSchemaVersion"/>..<see cref="MaxSchemaVersion"/> range arrives, notify via
/// <see cref="Channel"/>" (BIO-RPT-001-AC01's "versioned domain events" - a rule declares
/// which schema versions of an event shape it actually understands, so a future,
/// higher-numbered <see cref="DomainEventEnvelope.SchemaVersion"/> this rule was never
/// updated for is skipped rather than blindly processed as if it were the version the rule
/// was written against).
///
/// <see cref="RuleId"/> is a plain <see cref="Guid"/>, deliberately NOT one of ADR-0001
/// §1.2's six registered canonical-id types - the same reasoning
/// <c>Features.DeviceCertification.DeviceCertificationKey</c> already gives for its own
/// business key: minting a new prefix locally needs an ADR amendment and Kemi's approval,
/// outside this story's scope.
///
/// Immutable snapshot (ADR-0001 Decision 3): a mutation produces a new snapshot with
/// <see cref="Version"/> incremented by exactly 1, matching every other aggregate in this
/// codebase.
/// </summary>
public sealed record NotificationRule
{
    public required string RuleId { get; init; }
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public required AggregateType AggregateType { get; init; }
    public required NotificationChannel Channel { get; init; }
    public required int MinSchemaVersion { get; init; }
    public required int MaxSchemaVersion { get; init; }
    public required bool Enabled { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    public bool SupportsSchemaVersion(int schemaVersion) =>
        schemaVersion >= MinSchemaVersion && schemaVersion <= MaxSchemaVersion;
}

/// <summary>Input to register a rule (BIO-RPT-001-AC01). No <c>RuleId</c>/<c>TenantId</c>/
/// <c>Version</c> - assigned by the service, matching every other registration request in
/// this codebase (<c>DeviceRegistrationRequest</c>, <c>DeviceCertificationRegistrationRequest</c>).</summary>
public sealed record NotificationRuleRegistrationRequest(
    string Name,
    AggregateType AggregateType,
    NotificationChannel Channel,
    int MinSchemaVersion,
    int MaxSchemaVersion);

/// <summary>Enables or disables an existing rule (optimistic concurrency via
/// <see cref="ExpectedVersion"/>, matching every other status-change request in this
/// codebase).</summary>
public sealed record NotificationRuleStatusChangeRequest(bool Enabled, int ExpectedVersion);
