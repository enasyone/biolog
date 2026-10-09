namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>Session-scoped storage for <see cref="AuthenticityAssessment"/> aggregates - the same
/// provider-neutral seam BIO-DOC-001's own <c>IDocumentSessionStore</c> established for this epic.
/// One aggregate per document session; a caller who wants to update an existing one reads it,
/// mutates it in place via its own CAS-guarded methods, and does not need to call
/// <see cref="Save"/> again (the aggregate is a reference type held by the store) - <see cref="Save"/>
/// exists for the FIRST time an assessment is created for a session.</summary>
public interface IAuthenticityAssessmentStore
{
    AuthenticityAssessment? Get(string documentSessionId);

    /// <summary>Registers a newly-created aggregate. Throws if one already exists for this
    /// session - callers that want to add a new computed result to an EXISTING assessment use
    /// <see cref="AuthenticityAssessment.ReplaceResult"/> on the aggregate returned by
    /// <see cref="Get"/> instead.</summary>
    void Save(AuthenticityAssessment assessment);
}

public sealed class AuthenticityAssessmentAlreadyExistsException(string documentSessionId)
    : InvalidOperationException($"an authenticity assessment already exists for document session '{documentSessionId}'");
