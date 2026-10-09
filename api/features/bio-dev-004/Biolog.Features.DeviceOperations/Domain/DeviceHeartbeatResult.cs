namespace Biolog.Features.DeviceOperations;

/// <summary>BIO-DEV-004-AC01: the outcome of a single successful heartbeat poll - the
/// updated device snapshot (new <see cref="DeviceSnapshot.Version"/>, <c>LastSeenAt</c>,
/// <c>Health</c>) that was actually committed.</summary>
public sealed record DeviceHeartbeatResult(DeviceSnapshot Device);
