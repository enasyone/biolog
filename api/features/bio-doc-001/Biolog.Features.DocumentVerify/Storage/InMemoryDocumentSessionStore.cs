using System.Collections.Concurrent;
using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.DocumentVerify.Storage;

/// <summary>
/// Reference, in-process implementation of <see cref="IDocumentSessionStore"/> (BIO-DOC-001).
/// Thread-safe for concurrent sessions (a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed
/// by session id) but, like the domain aggregates it stores, relies on each session's own
/// <c>lock</c> for correctness of concurrent mutation against the SAME session - two overlapping
/// calls against the same session serialize on that session's monitor rather than racing, so the
/// optimistic-concurrency check inside each aggregate method is never itself racing a second
/// writer between the read and the write. A future MySQL-backed implementation gets this from
/// the database's own row locking / compare-and-set instead.
/// </summary>
public sealed class InMemoryDocumentSessionStore : IDocumentSessionStore
{
    private sealed class SessionRecord
    {
        public required DocumentSession Session;
        public readonly List<DocumentEvidence> Evidence = new();
        public readonly List<DocumentFieldEvidence> FieldEvidence = new();
        public readonly Dictionary<string, DocumentConflict> ConflictsByField = new(StringComparer.Ordinal);
        public readonly List<PersonFieldChangeProposal> Proposals = new();
        public readonly object Gate = new();
    }

    private readonly ConcurrentDictionary<string, SessionRecord> _sessionsById = new(StringComparer.Ordinal);

    public DocumentSession CreateSession(string id, string tenantId, string? personId, string nowUtc)
    {
        var session = DocumentSession.Open(id, tenantId, personId, nowUtc);
        var record = new SessionRecord { Session = session };
        if (!_sessionsById.TryAdd(id, record))
        {
            throw new InvalidOperationException($"document_session_id_already_exists: '{id}'");
        }
        return session;
    }

    public DocumentSession GetSession(string tenantId, string sessionId)
    {
        return Resolve(tenantId, sessionId).Session;
    }

    public DocumentSession LinkPerson(string tenantId, string sessionId, string personId, int expectedVersion, string nowUtc)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            record.Session.LinkPerson(personId, expectedVersion, nowUtc);
            return record.Session;
        }
    }

    public DocumentSession EnterManualReview(string tenantId, string sessionId, int expectedVersion, string nowUtc)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            record.Session.EnterManualReview(expectedVersion, nowUtc);
            return record.Session;
        }
    }

    public DocumentSession ResolveManualReview(string tenantId, string sessionId, int expectedVersion, string nowUtc)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            record.Session.ResolveManualReview(expectedVersion, nowUtc);
            return record.Session;
        }
    }

    public DocumentSession CloseSession(string tenantId, string sessionId, int expectedVersion, string nowUtc)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            record.Session.Close(expectedVersion, nowUtc);
            return record.Session;
        }
    }

    public DocumentEvidence AppendEvidence(
        string tenantId,
        string sessionId,
        string sha256Hash,
        string mimeType,
        int pageNumber,
        DocumentSide side,
        EvidenceSource source,
        string capturedAt,
        string receivedAt,
        EvidenceClassification classification)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            RequireNotClosed(record);
            var evidence = DocumentEvidence.Create(
                tenantId,
                sessionId,
                record.Evidence.Count + 1,
                sha256Hash,
                mimeType,
                pageNumber,
                side,
                source,
                capturedAt,
                receivedAt,
                classification);
            record.Evidence.Add(evidence);
            return evidence;
        }
    }

    public IReadOnlyList<DocumentEvidence> ListEvidence(string tenantId, string sessionId)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            return record.Evidence.ToList();
        }
    }

    public DocumentFieldEvidence AppendFieldEvidence(
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
        EvidenceClassification classification)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            RequireNotClosed(record);
            if (record.Evidence.All(e => e.SequenceNumber != sourceEvidenceSequenceNumber))
            {
                throw new ArgumentException(
                    $"unknown_source_evidence_sequence_number: no evidence #{sourceEvidenceSequenceNumber} on session '{sessionId}'",
                    nameof(sourceEvidenceSequenceNumber));
            }

            var fieldEvidence = DocumentFieldEvidence.Create(
                tenantId,
                sessionId,
                record.FieldEvidence.Count + 1,
                sourceEvidenceSequenceNumber,
                fieldName,
                rawValue,
                normalizedValue,
                confidence,
                source,
                boundingBox,
                extractedAt,
                classification);
            record.FieldEvidence.Add(fieldEvidence);
            return fieldEvidence;
        }
    }

    public IReadOnlyList<DocumentFieldEvidence> ListFieldEvidence(string tenantId, string sessionId)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            return record.FieldEvidence.ToList();
        }
    }

    public DocumentConflict RaiseConflict(
        string tenantId,
        string sessionId,
        string fieldName,
        IReadOnlyList<int> conflictingFieldEvidenceSequenceNumbers,
        string nowUtc)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            RequireNotClosed(record);
            if (record.ConflictsByField.TryGetValue(fieldName, out var existing) && existing.ReviewState != ManualReviewState.Resolved)
            {
                throw new InvalidOperationException(
                    $"document_conflict_already_open: field '{fieldName}' on session '{sessionId}' already has an unresolved conflict.");
            }
            var cited = new List<DocumentFieldEvidence>();
            foreach (var seq in conflictingFieldEvidenceSequenceNumbers)
            {
                var match = record.FieldEvidence.FirstOrDefault(f => f.SequenceNumber == seq);
                if (match is null)
                {
                    throw new ArgumentException(
                        $"unknown_field_evidence_sequence_number: no field evidence #{seq} on session '{sessionId}'",
                        nameof(conflictingFieldEvidenceSequenceNumbers));
                }
                // QA-01 (Seyi review): a cited sequence number alone proves nothing - it must
                // actually be evidence FOR the field this conflict names, and the cited records
                // must actually disagree, or a caller could open a "conflict" on unrelated or
                // identical values and fabricate manual-review provenance.
                if (match.FieldName != fieldName)
                {
                    throw new ArgumentException(
                        $"field_evidence_field_name_mismatch: field evidence #{seq} is for '{match.FieldName}', not '{fieldName}'",
                        nameof(conflictingFieldEvidenceSequenceNumbers));
                }
                cited.Add(match);
            }
            if (cited.Select(f => f.NormalizedValue).Distinct().Count() < 2)
            {
                throw new ArgumentException(
                    "conflicting_field_evidence_does_not_disagree: cited field evidence must contain at least two distinct normalized values",
                    nameof(conflictingFieldEvidenceSequenceNumbers));
            }

            var conflict = DocumentConflict.Raise(tenantId, sessionId, fieldName, conflictingFieldEvidenceSequenceNumbers, nowUtc);
            record.ConflictsByField[fieldName] = conflict;

            if (record.Session.Status != DocumentSessionStatus.ManualReviewPending)
            {
                record.Session.EnterManualReview(record.Session.Version, nowUtc);
            }

            return conflict;
        }
    }

    public IReadOnlyList<DocumentConflict> ListConflicts(string tenantId, string sessionId)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            return record.ConflictsByField.Values.ToList();
        }
    }

    public DocumentConflict ResolveConflict(string tenantId, string sessionId, string fieldName, string resolvedValue, string resolvedBy, string nowUtc)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            if (!record.ConflictsByField.TryGetValue(fieldName, out var conflict))
            {
                throw new InvalidOperationException($"document_conflict_not_found: field '{fieldName}' on session '{sessionId}'.");
            }

            conflict.Resolve(resolvedValue, resolvedBy, nowUtc, conflict.Version);

            var anyStillOpen = record.ConflictsByField.Values.Any(c => c.ReviewState != ManualReviewState.Resolved);
            if (!anyStillOpen && record.Session.Status == DocumentSessionStatus.ManualReviewPending)
            {
                record.Session.ResolveManualReview(record.Session.Version, nowUtc);
            }

            return conflict;
        }
    }

    public PersonFieldChangeProposal ProposePersonFieldChange(
        string tenantId,
        string sessionId,
        string personField,
        string proposedValue,
        int sourceFieldEvidenceSequenceNumber,
        string proposedAt)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            var personId = record.Session.PersonId
                ?? throw new InvalidOperationException(
                    $"document_session_not_person_linked: session '{sessionId}' has no linked person to propose a change for.");
            if (record.FieldEvidence.All(f => f.SequenceNumber != sourceFieldEvidenceSequenceNumber))
            {
                throw new ArgumentException(
                    $"unknown_field_evidence_sequence_number: no field evidence #{sourceFieldEvidenceSequenceNumber} on session '{sessionId}'",
                    nameof(sourceFieldEvidenceSequenceNumber));
            }

            var requiresManualReview = record.ConflictsByField.TryGetValue(personField, out var conflict)
                && conflict.ReviewState != ManualReviewState.Resolved;

            var proposal = PersonFieldChangeProposal.Propose(
                tenantId,
                sessionId,
                personId,
                personField,
                proposedValue,
                sourceFieldEvidenceSequenceNumber,
                requiresManualReview,
                proposedAt);
            record.Proposals.Add(proposal);
            return proposal;
        }
    }

    public IReadOnlyList<PersonFieldChangeProposal> ListPersonFieldChangeProposals(string tenantId, string sessionId)
    {
        var record = Resolve(tenantId, sessionId);
        lock (record.Gate)
        {
            return record.Proposals.ToList();
        }
    }

    private SessionRecord Resolve(string tenantId, string sessionId)
    {
        if (!_sessionsById.TryGetValue(sessionId, out var record) || record.Session.TenantId != tenantId)
        {
            // ADR-0002 §4.3: a cross-tenant access fails closed as not-found, identically to a
            // genuinely missing session - see DocumentSessionNotFoundException's own remarks.
            throw new DocumentSessionNotFoundException(tenantId, sessionId);
        }
        return record;
    }

    private static void RequireNotClosed(SessionRecord record)
    {
        if (record.Session.Status == DocumentSessionStatus.Closed)
        {
            throw new InvalidOperationException($"document_session_closed: session '{record.Session.Id}' accepts no further evidence.");
        }
    }
}
