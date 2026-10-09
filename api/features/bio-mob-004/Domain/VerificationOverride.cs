namespace Biolog.Features.MobileVerification.Domain;

/// <summary>
/// BIO-MOB-004-AC03: a Supervisor-role device's record that it manually admitted a person despite
/// a computed <c>deny</c>. Deliberately this feature's OWN domain object, not a mutation of
/// BIO-ACC-002's own <c>AccessDecisionEvent</c> - <see cref="DecisionEventId"/> is an opaque,
/// read-only correlation reference only (per Kemi's ownership-boundary ruling, 2026-09-30: a
/// story never mutates another story's owned aggregate). An override never changes what BIO-ACC-002
/// recorded; it is a separate, additional fact this feature records about its own workflow.
/// </summary>
public sealed record VerificationOverride
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string DecisionEventId { get; init; }
    public required string PersonId { get; init; }
    public required string DeviceId { get; init; }
    public required string ReasonCode { get; init; }
    public required string OverriddenAt { get; init; }
}

public interface IVerificationOverrideRepository
{
    Task CreateAsync(VerificationOverride entry, CancellationToken cancellationToken = default);
}

public sealed class InMemoryVerificationOverrideRepository : IVerificationOverrideRepository
{
    private readonly System.Collections.Concurrent.ConcurrentBag<VerificationOverride> _entries = new();

    public Task CreateAsync(VerificationOverride entry, CancellationToken cancellationToken = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }
}
