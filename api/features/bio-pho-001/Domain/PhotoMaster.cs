namespace Biolog.Features.Photography.Domain;

/// <summary>
/// The approved master photo for a PERSON (BIO-PHO-001-AC01), immutable and append-only per
/// ADR-0001 §3.3 ("PhotoMaster ... are immutable or append-only ... They carry no `version`
/// field and reject updates outright. Correcting them means appending a new record that
/// supersedes the old one and points at it").
///
/// <b>"Immutable/versioned" (AC01) reconciled with "no version field" (§3.3).</b> These are
/// not in tension: §3.3's prohibition is on the MUTABLE, compare-and-set <c>version</c> token
/// §3.1/§3.2 define for a mutating aggregate (there is nothing to compare-and-set against a
/// record that never updates). <see cref="Version"/> here is a plain, ever-increasing sequence
/// number identifying WHICH immutable row this is within one person's lineage - the same
/// resolution BIO-CRD-001's <c>BrandAsset</c> already used for an identical apparent
/// contradiction ("a re-upload creates a new version rather than mutating one"). Correcting a
/// photo means <see cref="IPhotoMasterRepository.AppendAsync"/>-ing a new, higher-numbered row;
/// the old one stays exactly as it was, which is what makes AC02/AC03 - a derivative or an
/// assignment that references a specific version - trustworthy.
///
/// <b>No canonical id of its own kind:</b> unlike PERSON (<c>per_</c>, a registered ADR-0001
/// type), a photo master has no dedicated identifier. Its natural key is
/// <c>(PersonId, Version)</c> - the same "no new registered type for a person-child aggregate"
/// choice BIO-IDN-002's <c>PersonProfile</c> made, for an identical reason: nothing about a
/// photo master needs to be named independently of the person and version that define it, and
/// adding a seventh registered identifier type would need an amending ADR and Kemi's approval
/// for no expressed need.
///
/// <b>Construction is internal (BIO-IDN-001-QA-01's lesson, applied here from the start).</b>
/// Every field is <c>get</c>-only - there is no <c>init</c> accessor anywhere on this type,
/// which is the structural proof of immutability: nothing in this assembly, let alone a
/// caller, can construct a <c>with</c> expression against it at all. The constructor is
/// <c>internal</c>, restricted to this assembly, <c>Biolog.Api</c> (DI registration) and this
/// project's own test assembly via <c>InternalsVisibleTo</c>.
///
/// <b>No PII value stored redundantly.</b> Attribution (who uploaded this) lives only in the
/// audit trail (<see cref="Audit.PhotoAuditEvent"/>), never duplicated onto this aggregate -
/// the same choice <c>Person</c>/<c>PersonProfile</c> make for their own mutations.
///
/// <b>BIO-PHO-001-QA-01 (Seyi's independent review).</b> A get-only <c>byte[]</c> property is
/// NOT actually immutable - the array it points at can still be mutated in place by anyone
/// holding a reference to it, through either of two channels: (1) the caller who constructed
/// this instance keeps its own reference to the array it passed in and mutates it afterwards,
/// or (2) any later reader of <see cref="Content"/> mutates the array it got back, corrupting
/// what every SUBSEQUENT reader sees too, since a plain auto-property hands out the live
/// backing array, not a copy. <see cref="Content"/> closes both: the constructor clones the
/// caller's array before storing it (closing channel 1), and the property clones the stored
/// array on every read (closing channel 2) - "defensive copy in, defensive copy out," the
/// standard idiom for a reference-typed field on an otherwise-immutable object.
/// </summary>
public sealed class PhotoMaster
{
    private readonly byte[] _content;

    public string PersonId { get; }
    public string TenantId { get; }
    public int Version { get; }
    public PhotoFormat Format { get; }
    public int WidthPx { get; }
    public int HeightPx { get; }
    public int SizeBytes { get; }
    public byte[] Content => (byte[])_content.Clone();
    public string CreatedAt { get; }

    internal PhotoMaster(string personId, string tenantId, int version, PhotoFormat format, int widthPx, int heightPx, byte[] content, string createdAt)
    {
        if (!Identifiers.CanonicalId.IsPersonId(personId))
        {
            throw new ArgumentException($"'{personId}' is not a well-formed person id (ADR-0001)", nameof(personId));
        }

        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "a photo master's version starts at 1");
        }

        if (widthPx <= 0 || heightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPx), "width and height must both be positive");
        }

        if (content.Length == 0)
        {
            throw new ArgumentException("photo content must not be empty", nameof(content));
        }

        PersonId = personId;
        TenantId = tenantId;
        Version = version;
        Format = format;
        WidthPx = widthPx;
        HeightPx = heightPx;
        SizeBytes = content.Length;
        _content = (byte[])content.Clone();
        CreatedAt = createdAt;
    }
}
