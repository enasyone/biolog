namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>In-process, in-memory implementation of <see cref="IAuthenticityAssessmentStore"/> -
/// the same "one instance, not a real tenant-scoped persistence layer" seam BIO-DOC-001's own
/// <c>InMemoryDocumentSessionStore</c> already documents for this epic; a real durable store is a
/// later story's job.</summary>
public sealed class InMemoryAuthenticityAssessmentStore : IAuthenticityAssessmentStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AuthenticityAssessment> _bySessionId = new();

    public AuthenticityAssessment? Get(string documentSessionId)
    {
        lock (_gate)
        {
            return _bySessionId.TryGetValue(documentSessionId, out var assessment) ? assessment : null;
        }
    }

    public void Save(AuthenticityAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        lock (_gate)
        {
            if (!_bySessionId.TryAdd(assessment.DocumentSessionId, assessment))
            {
                throw new AuthenticityAssessmentAlreadyExistsException(assessment.DocumentSessionId);
            }
        }
    }
}
