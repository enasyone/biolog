using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Biolog.Features.TenantRegistry.Api;
using Biolog.Features.TenantRegistry.Audit;
using Biolog.Features.TenantRegistry.Domain;
using Biolog.Features.TenantRegistry.Identifiers;

namespace Biolog.Features.TenantRegistry;

/// <summary>
/// Application service for BIO-TEN-001. Every public method here is control-plane scoped
/// (AC01): none takes or consumes a tenant context, and every audit event this emits carries
/// <c>tenant_id: null</c> (ADR-0002 §1.4).
///
/// BIO-TEN-001-QA-01 went through three rounds, and the sequence is worth keeping because
/// each round's fix is what exposed the next round's defect:
///
/// - **Round 1** committed to <see cref="ITenantRegistry"/> first, then audited, rolling back
///   on an audit failure. The rollback CAS was itself skippable by a concurrent writer:
///   writer A commits v2, blocks in a failing audit sink; writer B reads v2, commits v3;
///   A's rollback (v2→v1) now conflicts with v3 and was swallowed, permanently baking A's
///   unaudited field into v3 with no event ever describing it. Once a mutation is visible to
///   another writer, no CAS-based "undo" can promise removal, because "undo" and "someone
///   already built on it" are racing each other.
/// - **Round 2** audited BEFORE committing, so nothing uncommitted is ever visible to another
///   writer. That closed round 1's race, but created the mirror-image defect: the audit event
///   durably asserts <c>Outcome = Allowed</c> before the CAS commit runs, so if a *different*
///   writer wins that commit first, the audited writer's own <see cref="ITenantRegistry.ReplaceAsync"/>
///   correctly returns <c>concurrency_conflict</c> - but its permanent audit event still
///   falsely claims the change succeeded.
/// - **Round 3 (this one)** stops trying to order two independently-racing steps (audit,
///   commit) and instead removes the race between them: every mutating method now holds an
///   in-process, per-tenant-id lock (<see cref="LockTenantAsync"/>) across the ENTIRE
///   read-validate-audit-commit sequence for that tenant. Only one mutation of a given tenant
///   can be in flight in this process at a time, so by the time a candidate's audit event is
///   written, no other writer in this process could have raced ahead of it - the subsequent
///   <see cref="ITenantRegistry.ReplaceAsync"/>/<see cref="ITenantRegistry.CreateAsync"/> is
///   therefore guaranteed to observe exactly the state the audit event already describes, and
///   the audited outcome and the committed outcome can no longer disagree. This is a real
///   substitute for "one atomic transaction" in an in-memory, single-process store - an
///   aggregate-scoped lock, the same shape a real database gives a single row via row
///   locking - not a workaround that narrows the failure window.
///
/// **Scope this does and does not cover:** the lock lives on this instance
/// (<see cref="_tenantLocks"/>), and <see cref="BioTen001Module"/> registers
/// <see cref="TenantService"/> as a DI singleton - so there is exactly one instance, and
/// therefore one shared lock table, serving every request in this process. That is what
/// makes the guarantee real rather than accidental: two <see cref="TenantService"/>
/// instances constructed independently around the same <see cref="ITenantRegistry"/> would
/// NOT serialize against each other (see the tests that intentionally do this to isolate an
/// audit-sink failure from a concurrency scenario). The lock does not, and cannot, protect
/// against a second OS process or a second deployed instance racing the same store, because
/// there is no in-memory store shared across processes to protect: a later persistence story
/// backed by a real database inherits this same obligation and must provide the equivalent
/// guarantee (a row lock or serializable transaction) at that layer instead.
///
/// **Post-round-4 hardening** (Kemi heads-up, cross-referencing Seyi's finding in Emeka's
/// BIO-DEV-001 / QA-03): once <see cref="AuditAsync"/> has returned - the audit event is
/// confirmed durable - each mutation's final registry commit call passes
/// <see cref="CancellationToken.None"/>, not the caller's live token. The audit write is the
/// real point of no return; a cancellation signal arriving in the gap before the commit must
/// not be allowed to abort it, or the durable <c>Allowed</c> event would outlive a change
/// that never reached the store. <see cref="InMemoryTenantRegistry"/> does not check its
/// token today, so this is closed pre-emptively, not against a live bug here.
/// </summary>
public sealed partial class TenantService(ITenantRegistry registry, ITenantAuditSink auditSink)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    [GeneratedRegex("^[a-z0-9-]{1,64}$")]
    private static partial Regex RoutingRegionPattern();

    // One semaphore per tenant id, created on first use and kept for the process lifetime.
    // At this story's scale (a synthetic control-plane registry, not a high-cardinality
    // tenant-plane table) an entry per tenant that is never evicted is not a real memory
    // concern; a later story revisits this if tenant cardinality ever makes it one.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tenantLocks = new(StringComparer.Ordinal);

    /// <summary>
    /// Acquires the per-tenant-id lock described in the class header. Dispose the returned
    /// value (via <c>using</c>) to release it - always, on every exit path, including a
    /// thrown exception.
    /// </summary>
    private async Task<IDisposable> LockTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        var gate = _tenantLocks.GetOrAdd(tenantId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    public async Task<Tenant> CreateAsync(
        CreateTenantRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationErrorItem>();
        var name = ValidateName(request.Name, errors);
        var status = ValidateOptionalStatus(request.Status, TenantStatus.Active, errors);
        var licensePlan = ValidateLicensePlan(request.LicensePlan, errors);
        var licenseSeats = ValidateLicenseSeats(request.LicenseSeats, errors);
        var routingRegion = ValidateRoutingRegion(request.RoutingRegion, errors);
        ThrowIfInvalid(errors);

        var now = UtcInstant.Now();
        var tenant = new Tenant
        {
            Id = CanonicalId.NewTenantId(),
            Name = name!,
            Status = status,
            LicensePlan = licensePlan!,
            LicenseSeats = licenseSeats!.Value,
            RoutingRegion = routingRegion!,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // A freshly minted UUIDv7 cannot already be locked by another in-flight mutation,
        // but taking the lock here anyway keeps create on the identical audit-then-commit
        // discipline as every other mutation, rather than being a special case that has to
        // be re-justified on its own the next time this class is read.
        using var _ = await LockTenantAsync(tenant.Id, cancellationToken);

        await AuditAsync(actor, "tenant.create", tenant.Id, "tenant_created", correlationId, causationId: null,
            new Dictionary<string, (object?, string)>
            {
                ["name"] = (tenant.Name, "public"),
                ["status"] = (tenant.Status.ToWireString(), "internal"),
                ["license_plan"] = (tenant.LicensePlan, "internal"),
                ["license_seats"] = (tenant.LicenseSeats, "internal"),
                ["routing_region"] = (tenant.RoutingRegion, "internal"),
            },
            cancellationToken);

        // Once the audit write has been confirmed (AuditAsync above returned without
        // throwing), the audit event is the real point of no return: a cancellation
        // signal arriving in the gap before the commit must not be allowed to abort it,
        // or the durable Allowed event would outlive a change that never touched the
        // store. Only a genuine fault in the registry call itself may stop it here.
        return await registry.CreateAsync(tenant, CancellationToken.None);
    }

    public Task<Tenant?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        registry.GetAsync(id, cancellationToken);

    public async Task<(IReadOnlyList<Tenant> Items, string? NextCursor)> ListAsync(
        int? limit, string? cursor, string? statusFilterRaw, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationErrorItem>();
        var effectiveLimit = limit ?? DefaultLimit;
        if (effectiveLimit < 1 || effectiveLimit > MaxLimit)
        {
            errors.Add(new ValidationErrorItem("limit", "out_of_range", $"limit must be between 1 and {MaxLimit}"));
        }

        TenantStatus? statusFilter = null;
        if (statusFilterRaw is not null)
        {
            if (!TenantStatusExtensions.TryParse(statusFilterRaw, out var parsed))
            {
                errors.Add(new ValidationErrorItem("status", "invalid_format", "status must be one of active, suspended, disabled"));
            }
            else
            {
                statusFilter = parsed;
            }
        }

        string? afterId = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!PageCursor.TryDecode(cursor, out afterId))
            {
                errors.Add(new ValidationErrorItem("cursor", "invalid_format", "cursor is not a value this endpoint issued"));
            }
        }

        ThrowIfInvalid(errors);

        var (items, hasMore) = await registry.ListAsync(effectiveLimit, afterId, statusFilter, cancellationToken);
        var nextCursor = hasMore && items.Count > 0 ? PageCursor.Encode(items[^1].Id) : null;
        return (items, nextCursor);
    }

    public async Task<Tenant> UpdateAsync(
        string id, UpdateTenantRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        using var _ = await LockTenantAsync(id, cancellationToken);
        var current = await registry.GetAsync(id, cancellationToken) ?? throw TenantRegistryError.NotFound(id);

        var errors = new List<ValidationErrorItem>();
        if (request.Version is not int expectedVersion)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
        }

        var name = request.Name is null ? current.Name : ValidateName(request.Name, errors) ?? current.Name;
        var licensePlan = request.LicensePlan is null ? current.LicensePlan : ValidateLicensePlan(request.LicensePlan, errors) ?? current.LicensePlan;
        var licenseSeats = request.LicenseSeats is null ? current.LicenseSeats : ValidateLicenseSeats(request.LicenseSeats, errors) ?? current.LicenseSeats;
        var routingRegion = request.RoutingRegion is null ? current.RoutingRegion : ValidateRoutingRegion(request.RoutingRegion, errors) ?? current.RoutingRegion;
        ThrowIfInvalid(errors);

        if (request.Version!.Value != current.Version)
        {
            throw TenantRegistryError.ConcurrencyConflict(id, request.Version.Value, current.Version);
        }

        var changed = new Dictionary<string, (object?, string)>();
        if (name != current.Name) changed["name"] = (name, "public");
        if (licensePlan != current.LicensePlan) changed["license_plan"] = (licensePlan, "internal");
        if (licenseSeats != current.LicenseSeats) changed["license_seats"] = (licenseSeats, "internal");
        if (routingRegion != current.RoutingRegion) changed["routing_region"] = (routingRegion, "internal");

        if (changed.Count == 0)
        {
            // No-op update: nothing changed, so nothing is committed or audited, and the
            // version (and its optimistic-concurrency meaning) does not move - the same
            // posture UpdateStatusAsync already takes for a same-status transition.
            return current;
        }

        var updated = current with
        {
            Name = name,
            LicensePlan = licensePlan,
            LicenseSeats = licenseSeats,
            RoutingRegion = routingRegion,
            Version = current.Version + 1,
            UpdatedAt = UtcInstant.Now(),
        };

        await AuditAsync(actor, "tenant.update", updated.Id, "tenant_updated", correlationId, causationId: null, changed, cancellationToken);

        // Guaranteed to succeed: the per-tenant lock held since entry means no other writer
        // in this process could have changed `current` since we read it (class header,
        // round 3). A ConcurrencyConflict here would mean this class stopped taking that
        // lock somewhere, not a real concurrent writer.
        //
        // CancellationToken.None: the audit event above is already confirmed durable, so
        // it is the real point of no return - a cancellation signal arriving in this gap
        // must not abort the commit and leave that Allowed event outliving an update that
        // never touched the store.
        return await registry.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<Tenant> UpdateStatusAsync(
        string id, UpdateTenantStatusRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        using var _ = await LockTenantAsync(id, cancellationToken);
        var current = await registry.GetAsync(id, cancellationToken) ?? throw TenantRegistryError.NotFound(id);

        var errors = new List<ValidationErrorItem>();
        if (request.Version is not int)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
        }

        if (!TenantStatusExtensions.TryParse(request.Status, out var newStatus))
        {
            errors.Add(new ValidationErrorItem("status", "invalid_format", "status must be one of active, suspended, disabled"));
        }

        ThrowIfInvalid(errors);

        if (request.Version!.Value != current.Version)
        {
            throw TenantRegistryError.ConcurrencyConflict(id, request.Version.Value, current.Version);
        }

        if (newStatus == current.Status)
        {
            return current; // no-op transition: nothing changed, nothing to audit
        }

        var updated = current with { Status = newStatus, Version = current.Version + 1, UpdatedAt = UtcInstant.Now() };

        await AuditAsync(actor, "tenant.status_change", updated.Id, "tenant_status_changed", correlationId, causationId: null,
            new Dictionary<string, (object?, string)>
            {
                ["from_status"] = (current.Status.ToWireString(), "internal"),
                ["to_status"] = (newStatus.ToWireString(), "internal"),
            },
            cancellationToken);

        // See CreateAsync/UpdateAsync: once the audit write is confirmed, only a genuine
        // fault in the registry call may stop the commit - not a mere cancellation signal.
        return await registry.ReplaceAsync(current, updated, CancellationToken.None);
    }

    /// <summary>
    /// The "D" of CRUD, deliberately implemented as a soft delete: sets status to
    /// <see cref="TenantStatus.Disabled"/> and retains the record. ADR-0001's append-only,
    /// audit-everything ethos has no described procedure for physically erasing a tenant
    /// boundary, and doing so would orphan every tenant-plane binding ADR-0002 describes
    /// without a documented repair path - out of this story's scope. A tenant is already
    /// deactivated in every observable sense once disabled (AC03).
    /// </summary>
    public async Task<Tenant> SoftDeleteAsync(
        string id, int expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        using var _ = await LockTenantAsync(id, cancellationToken);
        var current = await registry.GetAsync(id, cancellationToken) ?? throw TenantRegistryError.NotFound(id);
        if (expectedVersion != current.Version)
        {
            throw TenantRegistryError.ConcurrencyConflict(id, expectedVersion, current.Version);
        }

        if (current.Status == TenantStatus.Disabled)
        {
            return current;
        }

        var updated = current with { Status = TenantStatus.Disabled, Version = current.Version + 1, UpdatedAt = UtcInstant.Now() };

        await AuditAsync(actor, "tenant.delete", updated.Id, "tenant_disabled", correlationId, causationId: null,
            new Dictionary<string, (object?, string)> { ["from_status"] = (current.Status.ToWireString(), "internal") },
            cancellationToken);

        // See CreateAsync/UpdateAsync: once the audit write is confirmed, only a genuine
        // fault in the registry call may stop the commit - not a mere cancellation signal.
        return await registry.ReplaceAsync(current, updated, CancellationToken.None);
    }

    /// <summary>
    /// Builds and durably writes the audit event for an administrative change BEFORE that
    /// change is committed to <see cref="ITenantRegistry"/> (see class header,
    /// BIO-TEN-001-QA-01).
    ///
    /// BIO-TEN-001-QA-01 round 4 (Seyi review): <see cref="ITenantAuditSink.WriteAsync"/>
    /// throwing does not prove the event was never durably persisted - a real store can
    /// commit and then fail to acknowledge. Treating every throw as "definitely did not
    /// happen" (round 3's behaviour) could leave a durably-written success event with no
    /// corresponding commit. On a throw, this method now checks ground truth via
    /// <see cref="ITenantAuditSink.ExistsAsync"/> before deciding: confirmed-present is
    /// treated as success (the caller proceeds to commit, which - protected by the same
    /// per-tenant lock the caller already holds - is guaranteed to succeed and will then
    /// truthfully match what the event already claims); confirmed-absent throws
    /// <c>audit_unavailable</c> with nothing committed, same as before. If the verification
    /// call ITSELF throws, that is a genuine, total audit-store outage rather than one
    /// ambiguous write - the same fail-closed <c>audit_unavailable</c> ADR-0002 §5 already
    /// accepts for a missing or unreachable sink, and the narrowest boundary this method can
    /// honestly close without a real cross-store transaction.
    ///
    /// This does not close every gap: a process failure between a confirmed-durable audit
    /// write and the registry commit that follows it is a true crash-consistency window no
    /// caller-side code can close without a durable write-ahead log/outbox spanning both
    /// stores - infrastructure that exists nowhere in this codebase yet. Kemi is drafting a
    /// cross-cutting ADR-0003 amendment for exactly this class of gap (informed by this
    /// finding and the identical shape in Emeka's BIO-DEV-001); closing it fully here would
    /// mean inventing shared, cross-story infrastructure inside one feature story's owned
    /// subtree, which this story is not positioned to do. See
    /// docs/features/bio-ten-001/README.md for the full, disclosed account.
    /// </summary>
    private async Task AuditAsync(
        AuditActor actor, string action, string tenantId, string reasonCode, string correlationId, string? causationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = TenantAuditEventBuilder.Build(
            actor, action, new AuditTarget("tenant", tenantId), AuditOutcome.Allowed, reasonCode, correlationId, causationId, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return; // confirmed: the write completed without throwing
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw TenantRegistryError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw TenantRegistryError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw on WriteAsync - proceed as a
        // success, so the caller's subsequent commit makes this event's claim true.
    }

    private static void ThrowIfInvalid(List<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw TenantRegistryError.Validation(errors);
        }
    }

    private static string? ValidateName(string? name, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(new ValidationErrorItem("name", "required", "name is required"));
            return null;
        }

        var trimmed = name.Trim();
        if (trimmed.Length > 200)
        {
            errors.Add(new ValidationErrorItem("name", "out_of_range", "name must be at most 200 characters"));
            return null;
        }

        return trimmed;
    }

    private static TenantStatus ValidateOptionalStatus(string? status, TenantStatus fallback, List<ValidationErrorItem> errors)
    {
        if (status is null)
        {
            return fallback;
        }

        if (!TenantStatusExtensions.TryParse(status, out var parsed))
        {
            errors.Add(new ValidationErrorItem("status", "invalid_format", "status must be one of active, suspended, disabled"));
            return fallback;
        }

        return parsed;
    }

    private static string? ValidateLicensePlan(string? plan, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(plan))
        {
            errors.Add(new ValidationErrorItem("license_plan", "required", "license_plan is required"));
            return null;
        }

        var trimmed = plan.Trim();
        if (trimmed.Length > 100)
        {
            errors.Add(new ValidationErrorItem("license_plan", "out_of_range", "license_plan must be at most 100 characters"));
            return null;
        }

        return trimmed;
    }

    private static int? ValidateLicenseSeats(int? seats, List<ValidationErrorItem> errors)
    {
        if (seats is not int value)
        {
            errors.Add(new ValidationErrorItem("license_seats", "required", "license_seats is required"));
            return null;
        }

        if (value < 0)
        {
            errors.Add(new ValidationErrorItem("license_seats", "out_of_range", "license_seats must be zero or greater"));
            return null;
        }

        return value;
    }

    private static string? ValidateRoutingRegion(string? region, List<ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            errors.Add(new ValidationErrorItem("routing_region", "required", "routing_region is required"));
            return null;
        }

        if (!RoutingRegionPattern().IsMatch(region))
        {
            errors.Add(new ValidationErrorItem(
                "routing_region", "invalid_format", "routing_region must match ^[a-z0-9-]{1,64}$ - a coarse placement tag, not free text"));
            return null;
        }

        return region;
    }
}
