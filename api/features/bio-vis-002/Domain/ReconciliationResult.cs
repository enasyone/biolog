namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>
/// BIO-VIS-002-F06's own remediation (Seyi round-5 FAIL): the outcome of one on-demand
/// <see cref="VisitorCredentialService.ReconcileStrandedCardsForPersonAsync"/> call - which
/// previously-stranded card sequence numbers were just neutralized versus which remain stranded
/// (still genuinely active, to be retried on the next opportunity).
/// </summary>
public sealed record ReconciliationResult(IReadOnlyList<int> Resolved, IReadOnlyList<int> StillUnresolved);
