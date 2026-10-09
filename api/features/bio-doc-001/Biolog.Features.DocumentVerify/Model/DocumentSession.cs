namespace Biolog.Features.DocumentVerify.Model;

/// <summary>
/// A single Document Verify session (BIO-DOC-001-AC01): the provider-neutral aggregate that
/// MRZ, barcode, OCR and NFC engines (BIO-DOC-002+) attach evidence to before any of those
/// engines exist. A session may start with no person attached ("pre-person onboarding" - e.g.
/// a kiosk capturing a document before the visitor's Person record is created) and later be
/// linked to exactly one person.
///
/// <para>
/// <b>Identifiers and timestamps are plain strings here, not a validated implementation of
/// ADR-0001's canonical-identifier/UTC-instant contracts.</b> ADR-0001's Consequences say to
/// "use <c>contracts/js/*</c> or a binding generated from <c>contracts/schemas/*</c> - do not
/// re-implement identifier parsing, instant formatting or version checks locally," and no .NET
/// binding of that JS-only contract exists yet anywhere in this codebase (confirmed: neither
/// <c>api/Biolog.Api/Tenancy/TenantContext.cs</c> nor any other file validates a canonical id or
/// instant format). Re-deriving that parser here, for this feature alone, would be exactly the
/// second, drifting implementation ADR-0001 warns against. This mirrors BIO-FND-008's identical,
/// explicitly documented choice for <c>TenantContext.cs</c>: a caller is expected to pass a
/// well-formed <c>dcs_</c>/<c>ten_</c>/<c>per_</c> id and a well-formed UTC instant string; this
/// type does not verify either shape. A future story that ports <c>contracts/js/identifiers.mjs</c>
/// and <c>contracts/js/clock.mjs</c> to .NET is the seam this type is written to accept without
/// changing its own shape.
/// </para>
///
/// <para>
/// What IS enforced here, in code, and covered by developer tests: the optimistic-concurrency
/// compare-and-set discipline ADR-0001 Decision 3 requires of every mutable tenant aggregate
/// (<see cref="Version"/> starts at 1, every mutation must state the version it believes it is
/// changing and bumps by exactly 1), and this story's own state-machine invariants (a session
/// already linked to one person cannot be silently re-linked to a different one; a closed session
/// accepts no further mutation).
/// </para>
/// </summary>
public sealed class DocumentSession
{
    public const int InitialVersion = 1;

    public string Id { get; }
    public string TenantId { get; }

    /// <summary>Null until <see cref="LinkPerson"/> is called (AC01: pre-person onboarding).</summary>
    public string? PersonId { get; private set; }

    public DocumentSessionStatus Status { get; private set; }

    /// <summary>ADR-0001 Decision 3.1: starts at 1, increments by exactly 1 per committed mutation.</summary>
    public int Version { get; private set; }

    public string CreatedAt { get; }
    public string UpdatedAt { get; private set; }

    private DocumentSession(string id, string tenantId, string? personId, DocumentSessionStatus status, int version, string createdAt, string updatedAt)
    {
        Id = id;
        TenantId = tenantId;
        PersonId = personId;
        Status = status;
        Version = version;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Opens a new session (AC01). <paramref name="personId"/> is optional: a null value is the
    /// pre-person onboarding path, not an error or a placeholder to be filled in later by a
    /// default.
    /// </summary>
    public static DocumentSession Open(string id, string tenantId, string? personId, string nowUtc)
    {
        RequireNonEmpty(id, nameof(id));
        RequireNonEmpty(tenantId, nameof(tenantId));
        if (personId is not null)
        {
            RequireNonEmpty(personId, nameof(personId));
        }
        RequireNonEmpty(nowUtc, nameof(nowUtc));

        var status = personId is null ? DocumentSessionStatus.Open : DocumentSessionStatus.PersonLinked;
        return new DocumentSession(id, tenantId, personId, status, InitialVersion, nowUtc, nowUtc);
    }

    /// <summary>
    /// Links this session to a person (AC01: the pre-person -> person-linked transition).
    /// Idempotent for the SAME person (a retried call with the same <paramref name="personId"/>
    /// succeeds without incrementing version again, matching the record's already-observed
    /// state); linking to a DIFFERENT person than the one already linked is refused outright -
    /// a document session names exactly one person once linked, it does not get silently
    /// reassigned to a second one.
    /// </summary>
    public void LinkPerson(string personId, int expectedVersion, string nowUtc)
    {
        RequireNonEmpty(personId, nameof(personId));
        RequireNonEmpty(nowUtc, nameof(nowUtc));
        RequireMutable();

        if (PersonId == personId)
        {
            return;
        }

        if (PersonId is not null)
        {
            throw new InvalidOperationException(
                $"document_session_already_linked_to_different_person: session '{Id}' is linked to '{PersonId}', cannot relink to '{personId}'.");
        }

        CheckVersion(expectedVersion);
        PersonId = personId;
        Status = DocumentSessionStatus.PersonLinked;
        Bump(nowUtc);
    }

    /// <summary>Raised when a <see cref="DocumentConflict"/> is opened against this session (AC05).</summary>
    public void EnterManualReview(int expectedVersion, string nowUtc)
    {
        RequireNonEmpty(nowUtc, nameof(nowUtc));
        RequireMutable();
        CheckVersion(expectedVersion);
        Status = DocumentSessionStatus.ManualReviewPending;
        Bump(nowUtc);
    }

    /// <summary>Returns a session from manual review once its conflicts are resolved (AC05).
    /// Only valid from <see cref="DocumentSessionStatus.ManualReviewPending"/>.</summary>
    public void ResolveManualReview(int expectedVersion, string nowUtc)
    {
        RequireNonEmpty(nowUtc, nameof(nowUtc));
        if (Status != DocumentSessionStatus.ManualReviewPending)
        {
            throw new InvalidOperationException(
                $"document_session_not_in_manual_review: session '{Id}' is '{Status}'.");
        }
        CheckVersion(expectedVersion);
        Status = PersonId is null ? DocumentSessionStatus.Open : DocumentSessionStatus.PersonLinked;
        Bump(nowUtc);
    }

    /// <summary>Terminal transition. No further evidence, field evidence or conflicts may be
    /// recorded against a closed session (enforced by callers/the store, not this type alone,
    /// since this type has no knowledge of its children - see
    /// <see cref="Storage.InMemoryDocumentSessionStore"/>).</summary>
    public void Close(int expectedVersion, string nowUtc)
    {
        RequireNonEmpty(nowUtc, nameof(nowUtc));
        RequireMutable();
        CheckVersion(expectedVersion);
        Status = DocumentSessionStatus.Closed;
        Bump(nowUtc);
    }

    private void RequireMutable()
    {
        if (Status == DocumentSessionStatus.Closed)
        {
            throw new InvalidOperationException($"document_session_closed: session '{Id}' accepts no further mutation.");
        }
    }

    private void CheckVersion(int expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyException(Id, expectedVersion, Version);
        }
    }

    private void Bump(string nowUtc)
    {
        Version += 1;
        UpdatedAt = nowUtc;
    }

    private static void RequireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("must be a non-empty string", paramName);
        }
    }
}

/// <summary>
/// ADR-0001 Decision 3.2's compare-and-set rejection ("expected_version differs -> 409,
/// concurrency_conflict, response carries the current version"). This type carries the same
/// information locally; mapping it to an HTTP 409/<c>concurrency_conflict</c> problem response
/// (ADR-0004 Decision 2) is a later story's job once this feature has an HTTP surface.
/// </summary>
public sealed class ConcurrencyException : InvalidOperationException
{
    public string AggregateId { get; }
    public int ExpectedVersion { get; }
    public int ActualVersion { get; }

    public ConcurrencyException(string aggregateId, int expectedVersion, int actualVersion)
        : base($"concurrency_conflict: '{aggregateId}' expected version {expectedVersion} but was {actualVersion}.")
    {
        AggregateId = aggregateId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}
