namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>
/// BIO-BIO-009-AC03: "Raw image retention defaults off unless authorised." A tenant-scoped
/// boolean, deliberately separate from <see cref="IRetentionPolicy"/>'s day-count shape: a raw
/// capture image is categorically more sensitive than a derived template (ADR-0003 Decision 3
/// classifies raw biometric images <c>secret</c>, never carried in an audit event), so whether it
/// may be kept AT ALL is its own policy question, answered before "for how long" is even asked.
/// </summary>
internal interface IRawImageRetentionPolicy
{
    /// <summary>Defaults to <c>false</c> for a tenant that has never called
    /// <see cref="AuthoriseAsync"/> - never throws for an unconfigured tenant.</summary>
    Task<bool> IsAuthorisedAsync(string tenantId, CancellationToken cancellationToken);

    Task AuthoriseAsync(string tenantId, CancellationToken cancellationToken);

    Task RevokeAsync(string tenantId, CancellationToken cancellationToken);
}
