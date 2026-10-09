namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>
/// Input to change a device's status (BIO-DEV-001-AC03). <see cref="ExpectedVersion"/> is
/// required on every call (ADR-0001 §3.2: a mutating write with no precondition is a
/// failure, not a last-write-wins fallback). <see cref="ActorType"/>/<see cref="ActorId"/>
/// are the caller's self-reported identity, accepted because no auth story has shipped a
/// verified session to derive them from yet - see <see cref="DeviceService.ChangeStatusAsync"/>.
/// </summary>
public sealed record DeviceStatusChangeRequest(
    DeviceStatus Status,
    int ExpectedVersion,
    string ActorType,
    string ActorId,
    string? CorrelationId);
