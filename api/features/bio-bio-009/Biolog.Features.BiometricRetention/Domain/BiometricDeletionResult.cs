namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>BIO-BIO-009-AC02/AC04: the outcome of one <see cref="TemplateDeletionRequest"/>.
/// <see cref="GalleryPropagated"/> is reported separately from <see cref="Outcome"/> because
/// cache propagation is best-effort (see <see cref="ITemplateDeletionGateway"/>'s own remarks) -
/// a cache-propagation failure never changes whether the record itself was deleted.</summary>
internal sealed record BiometricDeletionResult
{
    public required DeletionOutcome Outcome { get; init; }

    /// <summary>Null when <see cref="Outcome"/> is not <see cref="DeletionOutcome.Deleted"/> -
    /// there is nothing to propagate when the record was never removed.</summary>
    public required bool? GalleryPropagated { get; init; }
}
