namespace Biolog.Features.Photography.Domain;

/// <summary>
/// A print or display rendition generated from a specific, immutable <see cref="PhotoMaster"/>
/// version (BIO-PHO-001-AC02). Unlike <see cref="PhotoMaster"/>, a derivative is NOT on
/// ADR-0001 §3.3's append-only list - it is a regenerable cache artifact, not a source-of-truth
/// record, so <see cref="IPhotoDerivativeRepository.UpsertAsync"/> may replace the row for a
/// given <c>(PersonId, MasterVersion, Kind)</c> in place. This asymmetry - master never
/// replaceable, derivative always replaceable - is BIO-PHO-001-AC03's actual mechanism: see
/// <see cref="IPhotoDerivativeRepository"/>'s header for why regeneration can never reach the
/// master even by accident.
///
/// <b>References, never duplicates, the master's identity.</b> <see cref="MasterVersion"/> is
/// the pointer AC02 requires; this type carries no copy of the master's own fields (format,
/// dimensions, content) beyond what THIS derivative's own generation actually produced -
/// resolving "what master did this come from" always means following the pointer back to
/// <see cref="IPhotoMasterRepository"/>, never trusting a second, potentially-stale copy here.
///
/// <b>No canonical id of its own kind</b> - same reasoning as <see cref="PhotoMaster"/>'s
/// header. Natural key: <c>(PersonId, MasterVersion, Kind)</c>.
///
/// <b>BIO-PHO-001-QA-01 (Seyi's independent review).</b> Same defensive-copy-in,
/// defensive-copy-out treatment as <see cref="PhotoMaster.Content"/> - see that property's
/// header for the full rationale (a get-only <c>byte[]</c> alone hands out a live, mutable
/// reference; cloning on both construction and read closes both tamper channels).
/// </summary>
public sealed class PhotoDerivative
{
    private readonly byte[] _content;

    public string PersonId { get; }
    public string TenantId { get; }
    public int MasterVersion { get; }
    public DerivativeKind Kind { get; }
    public PhotoFormat Format { get; }
    public int WidthPx { get; }
    public int HeightPx { get; }
    public byte[] Content => (byte[])_content.Clone();
    public string GeneratedAt { get; }
    public string GeneratorTag { get; }

    internal PhotoDerivative(
        string personId, string tenantId, int masterVersion, DerivativeKind kind, PhotoFormat format,
        int widthPx, int heightPx, byte[] content, string generatedAt, string generatorTag)
    {
        if (!Identifiers.CanonicalId.IsPersonId(personId))
        {
            throw new ArgumentException($"'{personId}' is not a well-formed person id (ADR-0001)", nameof(personId));
        }

        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (masterVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(masterVersion), masterVersion, "a master version starts at 1");
        }

        if (widthPx <= 0 || heightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPx), "width and height must both be positive");
        }

        if (content.Length == 0)
        {
            throw new ArgumentException("derivative content must not be empty", nameof(content));
        }

        PersonId = personId;
        TenantId = tenantId;
        MasterVersion = masterVersion;
        Kind = kind;
        Format = format;
        WidthPx = widthPx;
        HeightPx = heightPx;
        _content = (byte[])content.Clone();
        GeneratedAt = generatedAt;
        GeneratorTag = generatorTag;
    }
}
