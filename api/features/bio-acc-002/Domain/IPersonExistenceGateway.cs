namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>Cross-story read seam onto BIO-IDN-001's own PersonRegistry - the same narrow existence-only gateway every story in this codebase's identity wave uses (BIO-IDN-002-QA-01's lesson).</summary>
public interface IPersonExistenceGateway
{
    Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default);
}
