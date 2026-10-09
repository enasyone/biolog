namespace Biolog.Features.DocumentVerify.Model;

/// <summary>
/// One immutable source-image evidence object attached to a <see cref="DocumentSession"/>
/// (BIO-DOC-001-AC02). Modelled as a C# <c>record</c> with <c>init</c>-only properties and no
/// mutator members, matching ADR-0001 Decision 3.3: raw document evidence is immutable and
/// append-only and "carries no version field". Correcting or superseding one means appending a
/// new <see cref="DocumentEvidence"/> that points at the original (a later story's concern, once
/// there is a real engine producing a "this replaces that" fact to record) - never mutating this
/// object.
///
/// <para>
/// <b>Identity without inventing a new canonical identifier type.</b> ADR-0001 registers exactly
/// six identifier prefixes (<c>ten</c>, <c>per</c>, <c>crd</c>, <c>dev</c>, <c>dcs</c>, <c>evt</c>)
/// and is explicit that "a feature story may not invent a prefix locally" - adding one requires a
/// registry entry in <c>contracts/schemas/identifier-registry.json</c> and Kemi's recorded
/// approval, which this story did not seek because a design that avoids the need entirely was
/// available: an evidence object's identity within its session is the pair
/// (<see cref="DocumentSessionId"/>, <see cref="SequenceNumber"/>) - the identical shape
/// ADR-0001 §1.6 already uses for the per-person card sequence ("1, 2, 3... never a canonical
/// identifier") - plus its own content hash, which is a stronger, tamper-evident identity anchor
/// for a piece of raw evidence than an opaque id would be. A future story that needs to reference
/// evidence from outside its own session (an audit event's <c>target</c>, ADR-0003 Decision 5) can
/// carry <c>(document_session_id, sequence_number)</c> or <c>sha256_hash</c> without needing a new
/// prefix either. If a later engine genuinely needs a standalone, cross-session canonical evidence
/// identifier, that is exactly the amending-ADR conversation ADR-0001 §1.2 describes - not a
/// decision this story makes unilaterally.
/// </para>
/// </summary>
public sealed record DocumentEvidence
{
    // QA-03 (Chioma acceptance audit): `public required X { get; init; }` alone does NOT make
    // `Create`'s validation mandatory - a caller can bypass it entirely with a plain object
    // initializer (`new DocumentEvidence { TenantId = "", ... }`), reaching every property's
    // `init` accessor directly since it was public. Every `init` below is now `private`: the
    // ONLY code that can ever run one is a member of this type - in practice, only `Create`.
    // `required` had to be dropped: C# refuses a `required` member whose accessor is less
    // visible than its containing (public) type (CS9032) - `Create`'s object initializer already
    // sets every property explicitly, so the compile-time "forgot a field" safety net `required`
    // gave up is a wash, not a new gap.
    public string TenantId { get; private init; } = null!;

    /// <summary>The owning <see cref="DocumentSession.Id"/>.</summary>
    public string DocumentSessionId { get; private init; } = null!;

    /// <summary>1-based, monotonically increasing per session. Not a canonical identifier - see
    /// the type-level remarks.</summary>
    public int SequenceNumber { get; private init; }

    /// <summary>Lowercase hex-encoded SHA-256 of the raw evidence bytes (64 characters).</summary>
    public string Sha256Hash { get; private init; } = null!;

    public string MimeType { get; private init; } = null!;

    /// <summary>1-based page number within the source document.</summary>
    public int PageNumber { get; private init; }

    public DocumentSide Side { get; private init; }

    public EvidenceSource Source { get; private init; }

    /// <summary>What the capturing device/client claims (ADR-0001 §2.4) - not authoritative.</summary>
    public string CapturedAt { get; private init; } = null!;

    /// <summary>Stamped on intake; authoritative for ordering (ADR-0001 §2.4).</summary>
    public string ReceivedAt { get; private init; } = null!;

    public EvidenceClassification Classification { get; private init; }

    // A record with a body (no positional parameter list) that declares no constructor of its
    // own gets an implicit PUBLIC parameterless one from the compiler regardless of how private
    // its properties are - `new DocumentEvidence()` would still compile from outside this type
    // and produce an all-default, entirely unvalidated instance (empty strings, sequence/page 0,
    // an unnamed default enum value). Declaring this private constructor is what suppresses that
    // synthesis; a caller outside this type now has no constructor to call at all.
    private DocumentEvidence()
    {
    }

    /// <summary>
    /// Validates and constructs a <see cref="DocumentEvidence"/>. This is the only code path that
    /// can produce one: every property's <c>init</c> accessor is <c>private</c>, so a caller
    /// outside this type cannot reach an object initializer at all (CS0272 at compile time), let
    /// alone one that skips this method's checks.
    /// </summary>
    public static DocumentEvidence Create(
        string tenantId,
        string documentSessionId,
        int sequenceNumber,
        string sha256Hash,
        string mimeType,
        int pageNumber,
        DocumentSide side,
        EvidenceSource source,
        string capturedAt,
        string receivedAt,
        EvidenceClassification classification)
    {
        RequireNonEmpty(tenantId, nameof(tenantId));
        RequireNonEmpty(documentSessionId, nameof(documentSessionId));
        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), sequenceNumber, "must be >= 1");
        }
        RequireSha256Hex(sha256Hash);
        RequireNonEmpty(mimeType, nameof(mimeType));
        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "must be >= 1");
        }
        RequireDefined(side, nameof(side));
        RequireDefined(source, nameof(source));
        RequireNonEmpty(capturedAt, nameof(capturedAt));
        RequireNonEmpty(receivedAt, nameof(receivedAt));
        RequireDefined(classification, nameof(classification));

        return new DocumentEvidence
        {
            TenantId = tenantId,
            DocumentSessionId = documentSessionId,
            SequenceNumber = sequenceNumber,
            Sha256Hash = sha256Hash,
            MimeType = mimeType,
            PageNumber = pageNumber,
            Side = side,
            Source = source,
            CapturedAt = capturedAt,
            ReceivedAt = receivedAt,
            Classification = classification,
        };
    }

    internal static void RequireNonEmpty(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("must be a non-empty string", paramName);
        }
    }

    /// <summary>Rejects a value cast from an integer outside the enum's declared members (e.g.
    /// <c>(DocumentSide)999</c>) - a bare range/equality check on the underlying int would not
    /// catch this, since the cast itself never throws.</summary>
    internal static void RequireDefined<TEnum>(TEnum value, string paramName) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"must be a defined {typeof(TEnum).Name} value");
        }
    }

    private static void RequireSha256Hex(string value)
    {
        if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit) || value.Any(char.IsUpper))
        {
            throw new ArgumentException("must be 64 lowercase hex characters", nameof(Sha256Hash));
        }
    }
}
