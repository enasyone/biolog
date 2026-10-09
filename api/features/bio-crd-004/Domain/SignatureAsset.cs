using Biolog.Features.CardObjects.Identifiers;

namespace Biolog.Features.CardObjects.Domain;

/// <summary>
/// BIO-CRD-004-AC04: "Signature assets are versioned." Immutable and append-only per ADR-0001
/// §3.3, the same "no version field" reconciliation BIO-PHO-001's <c>PhotoMaster</c> and
/// BIO-CRD-001's <c>BrandAsset</c> both already documented for an identical apparent
/// contradiction: <see cref="Version"/> is a plain, ever-increasing sequence number identifying
/// which immutable row this is within one signature's lineage - not the mutable, compare-and-set
/// <c>version</c> token §3.1/§3.2 define for a mutating aggregate. Correcting a signature means
/// appending a new, higher-numbered row via <see cref="ISignatureAssetRepository.AppendVersion"/>;
/// the old one stays exactly as it was.
///
/// <b>No canonical id of its own kind.</b> Like <c>PhotoMaster</c> and <c>BrandAsset</c>, a
/// signature asset has no dedicated ADR-0001 identifier type. Its natural key is
/// <c>(TenantId, LineageId, Version)</c> - <see cref="LineageId"/> is an opaque, caller-supplied
/// business key (e.g. a person id, a staff number, or any other stable signer reference this
/// story has no standing to define the shape of), not validated against any external registry
/// here since no cross-story existence-gateway dependency is declared for this story.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied from the start) - every
/// field is <c>get</c>-only, and the constructor is restricted to this assembly, <c>Biolog.Api</c>
/// and this project's own test assembly via <c>InternalsVisibleTo</c>.
///
/// <b>Defensive copy in, defensive copy out</b> (BIO-PHO-001-QA-01's lesson, applied from the
/// start) - <see cref="Content"/> clones on construction and on every read, so neither the
/// caller's original array nor a later reader's copy can corrupt what other readers see.
/// </summary>
public sealed class SignatureAsset
{
    private readonly byte[] _content;

    public string TenantId { get; }
    public string LineageId { get; }
    public int Version { get; }
    public byte[] Content => (byte[])_content.Clone();
    public string CreatedAt { get; }

    internal SignatureAsset(string tenantId, string lineageId, int version, byte[] content, string createdAt)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(lineageId))
        {
            throw new ArgumentException("lineageId must not be blank", nameof(lineageId));
        }

        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "a signature asset's version starts at 1");
        }

        if (content.Length == 0)
        {
            throw new ArgumentException("signature content must not be empty", nameof(content));
        }

        TenantId = tenantId;
        LineageId = lineageId;
        Version = version;
        _content = (byte[])content.Clone();
        CreatedAt = createdAt;
    }
}
