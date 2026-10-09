namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// Applies BIO-IDN-002-QA-01's lesson from the start (the same narrow, read-only BIO-IDN-001
/// existence gateway shape every identity-wave story establishes): a policy rule naming a
/// specific subject (<see cref="SubjectMatchKind.SpecificPerson"/>) must reference a person who
/// genuinely exists in the resolved tenant, checked before the rule is ever accepted into a
/// draft. <c>Biolog.Api</c>'s <c>BioAcc001Module</c> implements this by wrapping BIO-IDN-001's
/// own already-registered <c>PersonService.GetAsync</c> directly.
/// </summary>
internal interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}
