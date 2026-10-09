namespace Biolog.Features.DeviceOperations;

/// <summary>BIO-DEV-004-AC03/AC04: the outcome of a single remote job attempt, visible to
/// the caller - never silently swallowed, whichever way it resolves.</summary>
public sealed record RemoteJobResult(
    string DeviceId,
    RemoteJobType JobType,
    RemoteJobOutcome Outcome,
    string? Detail,
    DateTimeOffset ExecutedAt);
