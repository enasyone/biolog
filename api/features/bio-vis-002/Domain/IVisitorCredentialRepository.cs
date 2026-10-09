namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>
/// Storage for <see cref="VisitorCredential"/>. Every read/write takes <c>tenantId</c> explicitly
/// and asserts a matching record's own <see cref="VisitorCredential.TenantId"/> before returning
/// it - the same belt-and-braces rule every in-memory store in this codebase applies on top of the
/// service's own tenant-context check.
///
/// <c>internal</c> (same BIO-IDN-001-QA-01 discipline every identity-wave aggregate applies) -
/// restricted to this assembly plus <c>Biolog.Api</c> (DI registration) and
/// <c>Biolog.Features.VisitorCredentials.Tests</c> via <c>InternalsVisibleTo</c>.
/// </summary>
internal interface IVisitorCredentialRepository
{
    Task<VisitorCredential> CreateAsync(VisitorCredential credential, CancellationToken cancellationToken = default);

    Task<VisitorCredential?> GetAsync(string tenantId, string credentialId, CancellationToken cancellationToken = default);

    /// <summary>The currently-<see cref="VisitorCredentialStatus.Granted"/> credential for a visitor record, if any - a visitor can have at most one at a time (enforced by <see cref="VisitorCredentialService.GrantAsync"/>, not by this store).</summary>
    Task<VisitorCredential?> GetActiveForVisitorAsync(string tenantId, string visitorRecordId, CancellationToken cancellationToken = default);

    /// <summary>Every credential ever granted for a visitor record, oldest first - AC05's own history read.</summary>
    Task<IReadOnlyList<VisitorCredential>> ListForVisitorAsync(string tenantId, string visitorRecordId, CancellationToken cancellationToken = default);

    /// <summary>
    /// BIO-VIS-002-F03's own remediation (Seyi's round-2 FAIL): the credential row (in ANY status -
    /// Pending/Granted/Failed/Revoked) that already references this exact
    /// <c>(personId, cardClass, sequenceNumber)</c>, across EVERY visitor record for this tenant and
    /// person - or <c>null</c> if none does. <see cref="VisitorCredentialService.GrantAsync"/>'s own
    /// pre-adoption ownership check uses this to tell "genuinely unowned/stranded" and "my own
    /// visitor's earlier attempt" apart from "a DIFFERENT visitor record for the same person already
    /// legitimately owns this card" - the exact distinction the original adoption logic was missing.
    /// </summary>
    Task<VisitorCredential?> FindByCardAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expectedCurrent"/> (ADR-0001 Decision 3.2).</summary>
    Task<VisitorCredential> ReplaceAsync(VisitorCredential expectedCurrent, VisitorCredential updated, CancellationToken cancellationToken = default);
}
