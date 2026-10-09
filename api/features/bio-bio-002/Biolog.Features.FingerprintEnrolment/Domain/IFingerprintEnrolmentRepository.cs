namespace Biolog.Features.FingerprintEnrolment.Domain;

/// <summary>Persistence seam for <see cref="FingerprintEnrolmentSession"/>. This story ships only
/// <see cref="InMemoryFingerprintEnrolmentRepository"/>; a later story can implement this against
/// a real datastore without <see cref="Service.FingerprintEnrolmentService"/> changing shape -
/// the same separation-of-concerns BIO-BIO-001-AC03 established for biometric matching.
/// Internal - reachable only from this assembly, <c>Biolog.Api</c> (DI registration) and this
/// feature's own test project (an explicit <c>InternalsVisibleTo</c> seam), mirroring
/// BIO-IDN-001-QA-01's governance fix.</summary>
internal interface IFingerprintEnrolmentRepository
{
    Task<FingerprintEnrolmentSession> CreateAsync(FingerprintEnrolmentSession session, CancellationToken cancellationToken);

    Task<FingerprintEnrolmentSession?> GetAsync(string tenantId, string sessionId, CancellationToken cancellationToken);

    Task<FingerprintEnrolmentSession> ReplaceAsync(
        FingerprintEnrolmentSession expected, FingerprintEnrolmentSession updated, CancellationToken cancellationToken);

    /// <summary>BIO-BIO-009-AC02/AC04: removes a session outright. Returns <c>true</c> only when
    /// a session genuinely existed for this tenant and was removed; <c>false</c> for "no such
    /// session" (never an exception - the same cross-tenant-lookup-returns-null discipline
    /// <see cref="GetAsync"/> already uses, extended to deletion).</summary>
    Task<bool> DeleteAsync(string tenantId, string sessionId, CancellationToken cancellationToken);
}
