using Biolog.Features.Integrations.Audit;
using Biolog.Features.Integrations.Domain;
using Biolog.Features.Integrations.Identifiers;
using Biolog.Features.Integrations.Tenancy;

namespace Biolog.Features.Integrations;

public sealed record CreateMappingFields(
    SourceSystemType SourceSystem, SyncDirection SyncDirection, string DisplayName, IReadOnlyList<FieldMapping> FieldMappings);

public sealed record UpdateMappingFields(
    int ExpectedVersion, SyncDirection SyncDirection, string DisplayName, IReadOnlyList<FieldMapping> FieldMappings, bool Active);

/// <summary>
/// BIO-INT-002-AC01/AC02: CRUD for <see cref="ImportSourceMapping"/>, permission-gated
/// (<see cref="ManagePermission"/>) and provenance-verified (ADR-0006 Decision 3) - this
/// feature's own authorization decision, same discipline as every sibling story's config-write
/// endpoint. Implements ADR-0003 Decision 7's audit-atomicity pattern: per-mapping lock across
/// the whole read-validate-audit-commit sequence, audit before commit,
/// <see cref="IImportAuditSink.ExistsAsync"/> resolves an ambiguous write-throw, and
/// <see cref="CancellationToken.None"/> on the repository commit once the audit write is
/// confirmed durable.
/// </summary>
internal sealed class ImportMappingService(
    ICurrentTenantProvider tenantProvider, IImportSourceMappingRepository repository, IImportAuditSink auditSink,
    ImportLockRegistry lockRegistry, IPermissionGateway permissionGateway)
{
    public const string ManagePermission = "integrations.mapping_manage";

    public async Task<ImportSourceMapping> CreateAsync(
        CreateMappingFields fields, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequirePermissionAsync(tenantId, actor.Id, cancellationToken);

        var errors = ValidateDisplayNameAndFieldMappings(fields.DisplayName, fields.FieldMappings);
        ThrowIfInvalid(errors);

        var now = UtcInstant.Now();
        var mapping = new ImportSourceMapping(CanonicalId.NewLocalId(), tenantId)
        {
            SourceSystem = fields.SourceSystem,
            SyncDirection = fields.SyncDirection,
            DisplayName = fields.DisplayName,
            FieldMappings = fields.FieldMappings,
            Active = true,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        using var _ = await lockRegistry.AcquireAsync(tenantId, mapping.Id, cancellationToken);

        await AuditAsync(tenantId, actor, "integration.mapping_created", mapping.Id, "mapping_created", correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["source_system"] = (fields.SourceSystem.ToWireString(), "internal"),
                ["sync_direction"] = (fields.SyncDirection.ToWireString(), "internal"),
            },
            cancellationToken);

        return await repository.CreateAsync(mapping, CancellationToken.None);
    }

    public Task<ImportSourceMapping?> GetAsync(string mappingId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.GetAsync(tenantId, mappingId, cancellationToken);
    }

    public Task<IReadOnlyList<ImportSourceMapping>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return repository.ListByTenantAsync(tenantId, cancellationToken);
    }

    public async Task<ImportSourceMapping> UpdateAsync(
        string mappingId, UpdateMappingFields fields, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        await RequirePermissionAsync(tenantId, actor.Id, cancellationToken);

        var errors = ValidateDisplayNameAndFieldMappings(fields.DisplayName, fields.FieldMappings);
        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(tenantId, mappingId, cancellationToken);
        var current = await repository.GetAsync(tenantId, mappingId, cancellationToken) ?? throw ImportError.MappingNotFound(mappingId);

        if (fields.ExpectedVersion != current.Version)
        {
            throw ImportError.ConcurrencyConflict(mappingId, fields.ExpectedVersion, current.Version);
        }

        var updated = current with
        {
            SyncDirection = fields.SyncDirection,
            DisplayName = fields.DisplayName,
            FieldMappings = fields.FieldMappings,
            Active = fields.Active,
            Version = current.Version + 1,
            UpdatedAt = UtcInstant.Now(),
        };

        await AuditAsync(tenantId, actor, "integration.mapping_updated", mappingId, "mapping_updated", correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["sync_direction"] = (fields.SyncDirection.ToWireString(), "internal"),
                ["active"] = (fields.Active, "internal"),
            },
            cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    private async Task RequirePermissionAsync(string tenantId, string actorId, CancellationToken cancellationToken)
    {
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actorId, ManagePermission, cancellationToken))
        {
            throw ImportError.PermissionRequired(ManagePermission);
        }
    }

    private static List<Api.ValidationErrorItem> ValidateDisplayNameAndFieldMappings(string? displayName, IReadOnlyList<FieldMapping>? fieldMappings)
    {
        var errors = new List<Api.ValidationErrorItem>();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            errors.Add(new Api.ValidationErrorItem("display_name", "required", "display_name is required"));
        }

        if (fieldMappings is null || fieldMappings.Count == 0)
        {
            errors.Add(new Api.ValidationErrorItem("field_mappings", "required", "at least one field mapping is required"));
        }
        else
        {
            foreach (var (mapping, index) in fieldMappings.Select((m, i) => (m, i)))
            {
                if (string.IsNullOrWhiteSpace(mapping.SourceField))
                {
                    errors.Add(new Api.ValidationErrorItem($"field_mappings[{index}].source_field", "required", "source_field is required"));
                }

                if (string.IsNullOrWhiteSpace(mapping.PersonField))
                {
                    errors.Add(new Api.ValidationErrorItem($"field_mappings[{index}].person_field", "required", "person_field is required"));
                }
                else if (!PersonFieldCatalog.IsKnown(mapping.PersonField))
                {
                    errors.Add(new Api.ValidationErrorItem($"field_mappings[{index}].person_field", "unknown_person_field",
                        $"'{mapping.PersonField}' is not an importable PERSON field (allowed: {string.Join(", ", PersonFieldCatalog.KnownFields)})"));
                }
            }
        }

        return errors;
    }

    private static void ThrowIfInvalid(List<Api.ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw ImportError.Validation(errors);
        }
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string targetId, string reasonCode, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = ImportAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("import_source_mapping", targetId), AuditOutcome.Allowed, reasonCode, correlationId, null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw ImportError.AuditUnavailable();
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw ImportError.TenantContextMissing();
}

/// <summary>
/// BIO-INT-002-AC04: the only PERSON fields a mapping may ever target - exactly
/// <see cref="Domain.CreatePersonFields"/>'s own set, so a mapping can never be configured to
/// aim at a field this story has no authority to read/compare/populate (e.g. <c>status</c>,
/// which only <c>PersonService.UpdateStatusAsync</c> governs).
/// </summary>
public static class PersonFieldCatalog
{
    public static readonly IReadOnlyList<string> KnownFields =
        ["title", "first_name", "middle_name", "last_name", "preferred_name", "born_on"];

    public static bool IsKnown(string personField) => KnownFields.Contains(personField, StringComparer.Ordinal);
}
