using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.DocumentVerify.Storage;

/// <summary>
/// Storage seam for <see cref="DocumentSession"/> and its evidence (BIO-DOC-001). This
/// foundation ships <see cref="InMemoryDocumentSessionStore"/> as the only implementation - no
/// MySQL-backed one exists anywhere in this codebase yet (confirmed: no <c>DbContext</c>, no
/// connection string, no ORM package reference in <c>api/Biolog.Api</c>). A later, dedicated
/// persistence story implements this interface against a real tenant database (ADR-0002) and
/// proposes the schema through ADR-0005's migration-proposal process at that time; this
/// interface is the seam it plugs into without this feature's domain model changing shape.
/// </summary>
public interface IDocumentSessionStore
{
    DocumentSession CreateSession(string id, string tenantId, string? personId, string nowUtc);

    /// <summary>Throws <see cref="DocumentSessionNotFoundException"/> if no session with
    /// <paramref name="sessionId"/> exists for <paramref name="tenantId"/> - including when the
    /// session exists under a different tenant (ADR-0002 §4.3: fails closed as not-found).</summary>
    DocumentSession GetSession(string tenantId, string sessionId);

    DocumentSession LinkPerson(string tenantId, string sessionId, string personId, int expectedVersion, string nowUtc);

    DocumentSession EnterManualReview(string tenantId, string sessionId, int expectedVersion, string nowUtc);

    DocumentSession ResolveManualReview(string tenantId, string sessionId, int expectedVersion, string nowUtc);

    DocumentSession CloseSession(string tenantId, string sessionId, int expectedVersion, string nowUtc);

    DocumentEvidence AppendEvidence(
        string tenantId,
        string sessionId,
        string sha256Hash,
        string mimeType,
        int pageNumber,
        DocumentSide side,
        EvidenceSource source,
        string capturedAt,
        string receivedAt,
        EvidenceClassification classification);

    IReadOnlyList<DocumentEvidence> ListEvidence(string tenantId, string sessionId);

    DocumentFieldEvidence AppendFieldEvidence(
        string tenantId,
        string sessionId,
        int sourceEvidenceSequenceNumber,
        string fieldName,
        string rawValue,
        string normalizedValue,
        double confidence,
        FieldEvidenceSource source,
        BoundingBox? boundingBox,
        string extractedAt,
        EvidenceClassification classification);

    IReadOnlyList<DocumentFieldEvidence> ListFieldEvidence(string tenantId, string sessionId);

    /// <summary>Raises a conflict and moves the session into
    /// <see cref="DocumentSessionStatus.ManualReviewPending"/> (AC05).</summary>
    DocumentConflict RaiseConflict(
        string tenantId,
        string sessionId,
        string fieldName,
        IReadOnlyList<int> conflictingFieldEvidenceSequenceNumbers,
        string nowUtc);

    IReadOnlyList<DocumentConflict> ListConflicts(string tenantId, string sessionId);

    /// <summary>Resolves the named field's conflict. Returns the session to
    /// <see cref="DocumentSessionStatus.Open"/>/<see cref="DocumentSessionStatus.PersonLinked"/>
    /// once every conflict on the session is resolved; leaves it in
    /// <see cref="DocumentSessionStatus.ManualReviewPending"/> while any other field still has an
    /// unresolved conflict.</summary>
    DocumentConflict ResolveConflict(string tenantId, string sessionId, string fieldName, string resolvedValue, string resolvedBy, string nowUtc);

    /// <summary>AC04: proposes - never applies - a PERSON field change. Automatically carries
    /// <see cref="PersonFieldChangeProposal.RequiresManualReview"/> = true when an unresolved
    /// <see cref="DocumentConflict"/> exists for <paramref name="personField"/> in this session.</summary>
    PersonFieldChangeProposal ProposePersonFieldChange(
        string tenantId,
        string sessionId,
        string personField,
        string proposedValue,
        int sourceFieldEvidenceSequenceNumber,
        string proposedAt);

    IReadOnlyList<PersonFieldChangeProposal> ListPersonFieldChangeProposals(string tenantId, string sessionId);
}
