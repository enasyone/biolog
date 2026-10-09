namespace Biolog.Features.ParserRegistry.Model;

/// <summary>
/// One parser's full version history and current workflow state (BIO-DOC-005-AC02/AC03/AC04):
/// a mutable aggregate root following ADR-0001 Decision 3's optimistic-concurrency discipline
/// (<see cref="ConcurrencyVersion"/> starts at 1, every mutation states the version it believes it
/// is changing) - the same shape as BIO-DOC-001's <c>DocumentSession</c>. This
/// <see cref="ConcurrencyVersion"/> is a technical safety mechanism for THIS aggregate's own
/// writes; it is distinct from <see cref="ParserVersionContent.VersionNumber"/>, the BUSINESS
/// versioning BIO-DOC-005-AC04 asks for ("published parser changes create a new version").
///
/// <para>
/// Workflow: <see cref="Draft"/> creates version 1. <see cref="ReviseDraft"/> edits the latest
/// version in place while it is still <see cref="ParserStatus.Draft"/> (unpublished, untested);
/// once the latest is <see cref="ParserStatus.Tested"/> or <see cref="ParserStatus.Published"/>
/// its content is frozen, and revising it instead starts a brand new version - the only way
/// BIO-DOC-005-AC04's "changes create a new version" is satisfied. <see cref="RecordTestResult"/>
/// promotes a passing Draft to Tested (BIO-DOC-005-AC02's "tested"). <see cref="Publish"/>
/// promotes a Tested version to Published, requiring a different actor from the version's own
/// author (maker-checker, BIO-DOC-005-AC02's "through approval") - the same principle ADR-0006
/// names BIO-IDN-006 as the maker-checker precedent for.
/// </para>
/// </summary>
public sealed class ParserRecord
{
    public const int InitialConcurrencyVersion = 1;

    public string ParserId { get; }
    public ParserOwnerScope OwnerScope { get; }
    public int ConcurrencyVersion { get; private set; }
    public string CreatedAt { get; }
    public string UpdatedAt { get; private set; }
    public int? PublishedVersionNumber { get; private set; }

    private readonly List<ParserVersionContent> _versions = [];
    public IReadOnlyList<ParserVersionContent> Versions => _versions;
    public ParserVersionContent Latest => _versions[^1];

    private ParserRecord(string parserId, ParserOwnerScope ownerScope, string createdAt)
    {
        ParserId = parserId;
        OwnerScope = ownerScope;
        ConcurrencyVersion = InitialConcurrencyVersion;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public static ParserRecord Draft(
        string parserId,
        ParserOwnerScope ownerScope,
        ParserApplicability applicability,
        IReadOnlyList<ParserFieldMapping> fieldMappings,
        IReadOnlyList<ParserValidationRule> validationRules,
        string authorActorId,
        string nowUtc)
    {
        RequireNonEmpty(parserId, nameof(parserId));
        RequireNonEmpty(authorActorId, nameof(authorActorId));
        RequireNonEmpty(nowUtc, nameof(nowUtc));
        if (fieldMappings.Count == 0)
        {
            throw new ArgumentException("a parser must map at least one field", nameof(fieldMappings));
        }

        var record = new ParserRecord(parserId, ownerScope, nowUtc);
        record._versions.Add(new ParserVersionContent(
            1, applicability, fieldMappings, validationRules, ParserStatus.Draft,
            authorActorId, testedByActorId: null, publishedByActorId: null, nowUtc, publishedAt: null));
        return record;
    }

    /// <summary>Edits the parser's content. In place while the latest version is still Draft;
    /// otherwise starts a new Draft version (BIO-DOC-005-AC04) - the latest Tested/Published
    /// version's content is never altered.</summary>
    public void ReviseDraft(
        ParserApplicability applicability,
        IReadOnlyList<ParserFieldMapping> fieldMappings,
        IReadOnlyList<ParserValidationRule> validationRules,
        string authorActorId,
        int expectedConcurrencyVersion,
        string nowUtc)
    {
        RequireNonEmpty(authorActorId, nameof(authorActorId));
        RequireNonEmpty(nowUtc, nameof(nowUtc));
        if (fieldMappings.Count == 0)
        {
            throw new ArgumentException("a parser must map at least one field", nameof(fieldMappings));
        }
        CheckVersion(expectedConcurrencyVersion);

        var latest = Latest;
        if (latest.Status == ParserStatus.Draft)
        {
            _versions[^1] = new ParserVersionContent(
                latest.VersionNumber, applicability, fieldMappings, validationRules, ParserStatus.Draft,
                authorActorId, testedByActorId: null, publishedByActorId: null, latest.CreatedAt, publishedAt: null);
        }
        else
        {
            _versions.Add(new ParserVersionContent(
                latest.VersionNumber + 1, applicability, fieldMappings, validationRules, ParserStatus.Draft,
                authorActorId, testedByActorId: null, publishedByActorId: null, nowUtc, publishedAt: null));
        }

        Bump(nowUtc);
    }

    /// <summary>Records a test outcome for the latest version (BIO-DOC-005-AC02). A pass promotes
    /// Draft -&gt; Tested. A fail leaves it Draft, unrevised, so a caller can fix it via
    /// <see cref="ReviseDraft"/> and retest - never silently promoted.</summary>
    public void RecordTestResult(bool passed, string testedByActorId, int expectedConcurrencyVersion, string nowUtc)
    {
        RequireNonEmpty(testedByActorId, nameof(testedByActorId));
        RequireNonEmpty(nowUtc, nameof(nowUtc));
        CheckVersion(expectedConcurrencyVersion);

        var latest = Latest;
        if (latest.Status != ParserStatus.Draft)
        {
            throw new InvalidOperationException(
                $"parser_version_not_in_draft: version {latest.VersionNumber} of '{ParserId}' is '{latest.Status}'.");
        }

        if (passed)
        {
            _versions[^1] = new ParserVersionContent(
                latest.VersionNumber, latest.Applicability, latest.FieldMappings, latest.ValidationRules,
                ParserStatus.Tested, latest.AuthorActorId, testedByActorId, publishedByActorId: null,
                latest.CreatedAt, publishedAt: null);
        }

        Bump(nowUtc);
    }

    /// <summary>Publishes the latest version through approval (BIO-DOC-005-AC02): requires it to
    /// have passed testing, and requires the approver to be a different actor from the version's
    /// own author (maker-checker - no self-approval).</summary>
    public void Publish(string approverActorId, int expectedConcurrencyVersion, string nowUtc)
    {
        RequireNonEmpty(approverActorId, nameof(approverActorId));
        RequireNonEmpty(nowUtc, nameof(nowUtc));
        CheckVersion(expectedConcurrencyVersion);

        var latest = Latest;
        if (latest.Status != ParserStatus.Tested)
        {
            throw new InvalidOperationException(
                $"parser_version_not_tested: version {latest.VersionNumber} of '{ParserId}' is '{latest.Status}'.");
        }
        if (approverActorId == latest.AuthorActorId)
        {
            throw new InvalidOperationException(
                "self_approval_not_permitted: the approver must differ from the version's own author.");
        }

        _versions[^1] = new ParserVersionContent(
            latest.VersionNumber, latest.Applicability, latest.FieldMappings, latest.ValidationRules,
            ParserStatus.Published, latest.AuthorActorId, latest.TestedByActorId, approverActorId,
            latest.CreatedAt, publishedAt: nowUtc);
        PublishedVersionNumber = latest.VersionNumber;
        Bump(nowUtc);
    }

    public ParserVersionContent? GetPublishedVersion() =>
        PublishedVersionNumber is int number ? _versions.Single(v => v.VersionNumber == number) : null;

    private void CheckVersion(int expectedVersion)
    {
        if (expectedVersion != ConcurrencyVersion)
        {
            throw new ConcurrencyException(ParserId, expectedVersion, ConcurrencyVersion);
        }
    }

    private void Bump(string nowUtc)
    {
        ConcurrencyVersion += 1;
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

/// <summary>ADR-0001 Decision 3's compare-and-set rejection, the same shape as BIO-DOC-001's
/// <c>DocumentSession.ConcurrencyException</c>.</summary>
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
