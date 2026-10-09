namespace Biolog.Features.InstitutionalIdentifiers.Domain;

/// <summary>
/// A matriculation number, staff number or other tenant-defined business identifier PERSON
/// (BIO-IDN-001) holds in this tenant (BIO-IDN-003). ADR-0001 Decision 1.6: "Institutional
/// identifiers (matriculation number, staff number) ... are business identifiers. They are
/// attributes of a record, never its primary key, never a foreign key, and never a canonical
/// identifier." <c>contracts/schemas/identifier-registry.json</c>'s <c>business_identifiers</c>
/// block names both examples verbatim - this story is not minting a new ADR-0001 identifier
/// type; it is the story that models the thing ADR-0001 already excluded from getting one.
///
/// <b>AC01 - typed and tenant-scoped:</b> <see cref="Type"/> is one of
/// <see cref="IdentifierType"/>; every read/write goes through the resolved
/// <see cref="TenantId"/>, never a caller-supplied one (see <see cref="InstitutionalIdentifierService"/>).
///
/// <b>AC02 - uniqueness rules are configurable per type:</b> this type itself carries no
/// uniqueness policy - <see cref="IdentifierUniquenessRule"/> is a SEPARATE, tenant-configurable
/// aggregate <see cref="InstitutionalIdentifierService.AssignAsync"/> consults before creating a
/// record. See that type's header for why.
///
/// <b>AC03 - history/status is preserved:</b> <see cref="Value"/> is immutable once created -
/// unlike a profile's mutable hierarchy fields, an institutional identifier's VALUE never
/// changes in place. Correcting or reissuing one means the current record is
/// <see cref="Retire"/>d (see <see cref="IdentifierStatus"/>) and a NEW record is created at the
/// next <see cref="SequenceNumber"/> for the same <c>(PersonId, Type, CustomLabel)</c> - the
/// ADR-0001 identifier-registry's own named example, "the per-person physical card sequence",
/// is exactly this shape: a natural, per-person-per-type incrementing sequence, not a canonical
/// id. Nothing is ever deleted, so operational history that referenced a specific record (e.g.
/// an audit event citing <c>sequence_number</c> 1) is never orphaned by a later correction.
///
/// <b>AC04 - never a credential id:</b> this type has no field named anything like
/// <c>credential_id</c>, and this project defines no authentication/verify endpoint of any
/// kind - see <c>docs/features/bio-idn-003/README.md</c> "BIO-IDN-003-AC04" for the full
/// boundary against BIO-CRD-001's <c>crd_</c> aggregate and BIO-DEV-001's opaque
/// <c>vault://</c>-style device credential reference.
///
/// <b>Construction is internal</b> (BIO-IDN-001-QA-01's lesson, applied here from the start).
/// <see cref="PersonId"/>, <see cref="TenantId"/>, <see cref="Type"/>, <see cref="CustomLabel"/>,
/// <see cref="SequenceNumber"/> and <see cref="Value"/> are the identity of this aggregate and
/// are <c>get</c>-only - immutable for the object's whole life, including across a <c>with</c>
/// expression. The constructor is <c>internal</c>, restricted to this assembly,
/// <c>Biolog.Api</c> (DI registration) and this project's own test assembly via
/// <c>InternalsVisibleTo</c> - see <see cref="IInstitutionalIdentifierRepository"/> for why the
/// repository is internal too and independently re-validates at the persistence boundary.
///
/// <b>No canonical id of its own kind:</b> the natural key is
/// <c>(PersonId, Type, CustomLabel, SequenceNumber)</c> - the same "hostname as natural key"
/// shape BIO-TEN-002's <c>TenantDomain</c> and BIO-IDN-002's <c>PersonProfile</c> both used.
/// </summary>
public sealed record InstitutionalIdentifier
{
    public string PersonId { get; }
    public string TenantId { get; }
    public IdentifierType Type { get; }
    public string? CustomLabel { get; }
    public int SequenceNumber { get; }
    public string Value { get; }
    public required IdentifierStatus Status { get; init; }
    public string? RetiredAt { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    internal InstitutionalIdentifier(string personId, string tenantId, IdentifierType type, string? customLabel, int sequenceNumber, string value)
    {
        if (!Identifiers.CanonicalId.IsPersonId(personId))
        {
            throw new ArgumentException($"'{personId}' is not a well-formed person id (ADR-0001)", nameof(personId));
        }

        if (!Identifiers.CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        if (type == IdentifierType.TenantDefined && string.IsNullOrWhiteSpace(customLabel))
        {
            throw new ArgumentException("custom_label is required when type is TENANT_DEFINED", nameof(customLabel));
        }

        if (type != IdentifierType.TenantDefined && customLabel is not null)
        {
            throw new ArgumentException("custom_label must be null unless type is TENANT_DEFINED", nameof(customLabel));
        }

        if (sequenceNumber < 1)
        {
            throw new ArgumentException("sequence_number must be at least 1", nameof(sequenceNumber));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("value must not be blank", nameof(value));
        }

        PersonId = personId;
        TenantId = tenantId;
        Type = type;
        CustomLabel = customLabel;
        SequenceNumber = sequenceNumber;
        Value = value;
    }
}
