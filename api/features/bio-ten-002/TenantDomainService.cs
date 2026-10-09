using System.Collections.Concurrent;
using Biolog.Features.TenantDomainRegistry.Api;
using Biolog.Features.TenantDomainRegistry.Audit;
using Biolog.Features.TenantDomainRegistry.Domain;
using Biolog.Features.TenantDomainRegistry.Identifiers;
using Biolog.Features.TenantDomainRegistry.Resolution;

namespace Biolog.Features.TenantDomainRegistry;

/// <summary>
/// Application service for BIO-TEN-002. Every mutating method here follows ADR-0003
/// Decision 7's mandated pattern for "must be both committed and audited, with no shared
/// transaction between the two stores" - the same pattern BIO-TEN-001 arrived at over five
/// review rounds and which this ADR now codifies as a cross-cutting requirement rather than a
/// per-story invention:
///
/// 1. Per-aggregate, in-process lock held across the whole read-validate-audit-commit
///    sequence (<see cref="LockHostnameAsync"/> / <see cref="LockTenantAsync"/> - see "Two
///    lock scopes, not one" below for why there are two).
/// 2. Audit BEFORE commit: nothing is ever visible to another writer before its audit event
///    is confirmed durable.
/// 3. On an ambiguous <see cref="ITenantDomainAuditSink.WriteAsync"/> throw, verify via
///    <see cref="ITenantDomainAuditSink.ExistsAsync"/> before deciding (<see cref="AuditAsync"/>).
/// 4. Once the audit event is confirmed, the commit call passes <see cref="CancellationToken.None"/>,
///    not the caller's token - audit is the genuine point of no return.
///
/// <b>Two lock scopes, not one.</b> Every mutation here touches either a brand-new hostname
/// (<see cref="RegisterAsync"/>) or an existing tenant's set of domains
/// (<see cref="UpdateStatusAsync"/>, <see cref="SetPrimaryAsync"/>) - and those are genuinely
/// different aggregates:
///
/// <list type="bullet">
/// <item><b>Register locks by hostname.</b> Its only real conflict is "did another Register
/// call claim this exact hostname first" - a conflict that can come from ANY tenant, not just
/// the one in this request (BIO-TEN-002-AC04 is a global, cross-tenant invariant). A
/// tenant-scoped lock could not serialize two different tenants racing for the same hostname;
/// a hostname-scoped lock can, because it is keyed by the one thing both racing calls agree
/// on. (The registry's own <c>TryAdd</c> also enforces uniqueness atomically regardless of
/// any lock - the lock's job here is solely to keep Register's own audit-then-commit honest,
/// not to be uniqueness's only guard.)</item>
/// <item><b>Verify/suspend/retire/set-primary lock by tenant id, not by hostname.</b>
/// <see cref="SetPrimaryAsync"/> must atomically flip <c>is_primary</c> off one existing
/// domain and on another for the SAME tenant - genuinely a two-record mutation, so the real
/// aggregate boundary is "this tenant's domain set", not one domain. Locking the other three
/// single-record operations by hostname instead (a narrower, more tempting scope) would let
/// one of them interleave with a same-tenant <see cref="SetPrimaryAsync"/> using a DIFFERENT
/// lock than the one protecting that other domain's read - reproducing exactly the
/// audit-says-Allowed-but-commit-fails shape ADR-0003 Decision 7 exists to close, just via a
/// lock-scope mismatch instead of a missing lock. Using the same key (tenant id) for all four
/// closes this: nothing touching any domain of a tenant can ever run concurrently with
/// anything else touching any domain of that same tenant.</item>
/// </list>
///
/// A hostname's <see cref="Domain.TenantDomain.TenantId"/> is immutable for the life of the
/// record (see that type's header), which is what makes it safe to read it once, OUTSIDE any
/// lock, purely to learn which tenant lock to take - the authoritative state used for
/// validation, auditing and committing is always re-read AFTER the lock is held, never the
/// pre-lock read.
///
/// <b>Deferred: hostname reuse after retire.</b> A <see cref="TenantDomainStatus.Retired"/>
/// record's hostname is never freed for a different tenant to claim - <see cref="RegisterAsync"/>
/// rejects any hostname that already has a record, regardless of that record's status. Freeing
/// a retired hostname safely would need to consider in-flight cached resolutions elsewhere in
/// the platform and is a bigger question than flipping a status enum; no AC requires it, and
/// building it speculatively would be exactly the unrequested feature this story's own
/// guidance warns against. A later story can add reuse if a real need appears.
///
/// <b>Deferred: no "reinstate" action.</b> Only PENDING_VERIFICATION→VERIFIED (verify),
/// VERIFIED→SUSPENDED (suspend) and {PENDING_VERIFICATION, VERIFIED, SUSPENDED}→RETIRED
/// (retire) are implemented (<see cref="UpdateStatusAsync"/>'s transition table). Reactivating
/// a suspended or retired domain has no AC asking for it; the honest answer today is "not
/// supported", not a silently-permitted transition.
/// </summary>
public sealed class TenantDomainService(ITenantDomainRegistry registry, ITenantDomainAuditSink auditSink)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostnameLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tenantLocks = new(StringComparer.Ordinal);

    private Task<IDisposable> LockHostnameAsync(string hostname, CancellationToken cancellationToken) =>
        LockAsync(_hostnameLocks, hostname, cancellationToken);

    private Task<IDisposable> LockTenantAsync(string tenantId, CancellationToken cancellationToken) =>
        LockAsync(_tenantLocks, tenantId, cancellationToken);

    private static async Task<IDisposable> LockAsync(
        ConcurrentDictionary<string, SemaphoreSlim> table, string key, CancellationToken cancellationToken)
    {
        var gate = table.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    public async Task<Domain.TenantDomain> RegisterAsync(
        RegisterDomainRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationErrorItem>();
        var hostname = HostNormalizer.Normalize(request.Hostname);
        if (hostname is null)
        {
            errors.Add(new ValidationErrorItem("hostname", "invalid_format", "hostname must be a well-formed ASCII DNS hostname"));
        }

        if (!CanonicalId.IsTenantId(request.TenantId))
        {
            errors.Add(new ValidationErrorItem("tenant_id", "invalid_format", "tenant_id must be a canonical ten_ identifier"));
        }

        if (errors.Count > 0)
        {
            throw TenantDomainError.Validation(errors);
        }

        using var _ = await LockHostnameAsync(hostname!, cancellationToken);

        if (await registry.GetByHostnameAsync(hostname!, cancellationToken) is not null)
        {
            // Failed validation (in the sense that matters to the caller): no commit, no
            // audit - mirrors "a failed validation records no audit event" everywhere else in
            // this codebase's audited-mutation stories.
            throw TenantDomainError.DuplicateHostname(hostname!);
        }

        var now = UtcInstant.Now();
        var domain = new Domain.TenantDomain
        {
            Hostname = hostname!,
            TenantId = request.TenantId!,
            Status = TenantDomainStatus.PendingVerification,
            IsPrimary = false, // only SetPrimaryAsync, on an already-VERIFIED domain, may set this
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await AuditAsync(actor, "tenant_domain.register", domain.Hostname, "tenant_domain_registered", correlationId, causationId: null,
            new Dictionary<string, (object?, string)>
            {
                ["hostname"] = (domain.Hostname, "public"),
                ["tenant_id"] = (domain.TenantId, "internal"),
            },
            cancellationToken);

        return await registry.CreateAsync(domain, CancellationToken.None);
    }

    public Task<Domain.TenantDomain?> GetAsync(string hostname, CancellationToken cancellationToken = default)
    {
        var normalized = HostNormalizer.Normalize(hostname);
        return normalized is null
            ? Task.FromResult<Domain.TenantDomain?>(null)
            : registry.GetByHostnameAsync(normalized, cancellationToken);
    }

    public Task<IReadOnlyList<Domain.TenantDomain>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        registry.ListByTenantAsync(tenantId, cancellationToken);

    /// <summary>
    /// BIO-TEN-002-AC02/AC03: the resolution LOGIC ADR-0002 Decision 1.3's <c>host</c>
    /// channel specifies - exact match, case-normalised, only <c>VERIFIED</c> resolves. This
    /// method is not, on its own, "the host channel is live": nothing calls it from the
    /// packaged API's real request pipeline yet (see <see cref="Api.TenantDomainEndpoints"/>'s
    /// header, BIO-TEN-002-QA-01). <paramref name="rawHost"/> must be the caller's real
    /// authority (e.g. an HTTP <c>Host</c> header) - never a value taken from a query
    /// parameter, a body field, or any other caller-controlled channel ADR-0002 §1.1 excludes.
    /// Returns the resolved domain (its <c>TenantId</c> is the resolved tenant) or throws
    /// <see cref="TenantDomainError.HostNotRecognised"/> - which deliberately never
    /// distinguishes "no such hostname" from "hostname exists but is not VERIFIED" (see that
    /// error's own header). Read-only: no lock, no audit. See
    /// docs/features/bio-ten-002/README.md "Scope: resolution audit" for why a failed
    /// resolution is not itself an audited event in this story.
    /// </summary>
    public async Task<Domain.TenantDomain> ResolveAsync(string? rawHost, CancellationToken cancellationToken = default)
    {
        var hostname = HostNormalizer.Normalize(rawHost);
        if (hostname is null)
        {
            throw TenantDomainError.HostNotRecognised();
        }

        var domain = await registry.GetByHostnameAsync(hostname, cancellationToken);
        if (domain is null || domain.Status != TenantDomainStatus.Verified)
        {
            throw TenantDomainError.HostNotRecognised();
        }

        return domain;
    }

    private static readonly IReadOnlyDictionary<(TenantDomainStatus From, TenantDomainStatus To), (string Action, string ReasonCode)> Transitions =
        new Dictionary<(TenantDomainStatus, TenantDomainStatus), (string, string)>
        {
            [(TenantDomainStatus.PendingVerification, TenantDomainStatus.Verified)] = ("tenant_domain.verify", "tenant_domain_verified"),
            [(TenantDomainStatus.Verified, TenantDomainStatus.Suspended)] = ("tenant_domain.suspend", "tenant_domain_suspended"),
            [(TenantDomainStatus.PendingVerification, TenantDomainStatus.Retired)] = ("tenant_domain.retire", "tenant_domain_retired"),
            [(TenantDomainStatus.Verified, TenantDomainStatus.Retired)] = ("tenant_domain.retire", "tenant_domain_retired"),
            [(TenantDomainStatus.Suspended, TenantDomainStatus.Retired)] = ("tenant_domain.retire", "tenant_domain_retired"),
        };

    public async Task<Domain.TenantDomain> UpdateStatusAsync(
        string hostname, UpdateDomainStatusRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var normalized = HostNormalizer.Normalize(hostname) ?? throw TenantDomainError.NotFound(hostname);
        var errors = new List<ValidationErrorItem>();
        if (request.Version is not int)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
        }

        if (!TenantDomainStatusExtensions.TryParse(request.Status, out var requestedStatus))
        {
            errors.Add(new ValidationErrorItem("status", "invalid_format", "status must be one of PENDING_VERIFICATION, VERIFIED, SUSPENDED, RETIRED"));
        }

        ThrowIfInvalid(errors);

        // Read once, unlocked, purely to learn which tenant lock to take - see class header.
        var initial = await registry.GetByHostnameAsync(normalized, cancellationToken) ?? throw TenantDomainError.NotFound(normalized);
        using var _ = await LockTenantAsync(initial.TenantId, cancellationToken);

        var current = await registry.GetByHostnameAsync(normalized, cancellationToken) ?? throw TenantDomainError.NotFound(normalized);
        if (request.Version!.Value != current.Version)
        {
            throw TenantDomainError.ConcurrencyConflict(normalized, request.Version.Value, current.Version);
        }

        if (requestedStatus == current.Status)
        {
            return current; // no-op transition: nothing changed, nothing to audit
        }

        if (!Transitions.TryGetValue((current.Status, requestedStatus), out var transition))
        {
            throw TenantDomainError.Validation([new ValidationErrorItem(
                "status", "invalid_status_transition",
                $"cannot transition from {current.Status.ToWireString()} to {requestedStatus.ToWireString()}")]);
        }

        var updated = current with { Status = requestedStatus, Version = current.Version + 1, UpdatedAt = UtcInstant.Now() };

        await AuditAsync(actor, transition.Action, updated.Hostname, transition.ReasonCode, correlationId, causationId: null,
            new Dictionary<string, (object?, string)>
            {
                ["hostname"] = (updated.Hostname, "public"),
                ["from_status"] = (current.Status.ToWireString(), "internal"),
                ["to_status"] = (requestedStatus.ToWireString(), "internal"),
            },
            cancellationToken);

        // Guaranteed to succeed: the per-tenant lock held since before the re-read means no
        // other writer in this process could have changed `current` since - see class header.
        return await registry.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<Domain.TenantDomain> SetPrimaryAsync(
        string hostname, SetPrimaryDomainRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var normalized = HostNormalizer.Normalize(hostname) ?? throw TenantDomainError.NotFound(hostname);
        if (request.Version is not int)
        {
            throw TenantDomainError.Validation([new ValidationErrorItem("version", "required", "version is required so a stale write can be detected")]);
        }

        var initial = await registry.GetByHostnameAsync(normalized, cancellationToken) ?? throw TenantDomainError.NotFound(normalized);
        using var _ = await LockTenantAsync(initial.TenantId, cancellationToken);

        var target = await registry.GetByHostnameAsync(normalized, cancellationToken) ?? throw TenantDomainError.NotFound(normalized);
        if (request.Version!.Value != target.Version)
        {
            throw TenantDomainError.ConcurrencyConflict(normalized, request.Version.Value, target.Version);
        }

        if (target.Status != TenantDomainStatus.Verified)
        {
            throw TenantDomainError.Validation([new ValidationErrorItem(
                "status", "invalid_status_transition", "only a VERIFIED domain may be set primary")]);
        }

        if (target.IsPrimary)
        {
            return target; // already the tenant's primary domain: no-op, nothing to audit
        }

        var siblings = await registry.ListByTenantAsync(target.TenantId, cancellationToken);
        var oldPrimary = siblings.FirstOrDefault(d => d.IsPrimary && !string.Equals(d.Hostname, target.Hostname, StringComparison.Ordinal));

        var now = UtcInstant.Now();
        var newTarget = target with { IsPrimary = true, Version = target.Version + 1, UpdatedAt = now };

        await AuditAsync(actor, "tenant_domain.primary_changed", target.Hostname, "tenant_domain_primary_changed", correlationId, causationId: null,
            new Dictionary<string, (object?, string)>
            {
                ["from_hostname"] = (oldPrimary?.Hostname, "public"),
                ["to_hostname"] = (target.Hostname, "public"),
            },
            cancellationToken);

        // Both commits below are guaranteed to succeed: the per-tenant lock held since before
        // the re-read above means no other writer in this process could have changed either
        // record since - see class header ("Two lock scopes, not one").
        if (oldPrimary is not null)
        {
            var clearedOld = oldPrimary with { IsPrimary = false, Version = oldPrimary.Version + 1, UpdatedAt = now };
            await registry.ReplaceAsync(oldPrimary, clearedOld, CancellationToken.None);
        }

        return await registry.ReplaceAsync(target, newTarget, CancellationToken.None);
    }

    private static void ThrowIfInvalid(IReadOnlyList<ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw TenantDomainError.Validation(errors);
        }
    }

    /// <summary>
    /// Builds and durably writes the audit event for an administrative change BEFORE that
    /// change is committed to <see cref="ITenantDomainRegistry"/> (see class header,
    /// ADR-0003 Decision 7).
    ///
    /// On an ambiguous <see cref="ITenantDomainAuditSink.WriteAsync"/> throw, verifies ground
    /// truth via <see cref="ITenantDomainAuditSink.ExistsAsync"/> rather than assuming
    /// failure: confirmed-present is treated as success (the lock-protected commit that
    /// follows then makes the event's claim true); confirmed-absent fails closed with nothing
    /// committed; <see cref="ITenantDomainAuditSink.ExistsAsync"/> itself throwing is a
    /// genuine, total audit-store outage - the same fail-closed boundary ADR-0002 §5 already
    /// accepts for a missing or unreachable sink (ADR-0003 Decision 7, element 4).
    /// </summary>
    private async Task AuditAsync(
        AuditActor actor, string action, string hostname, string reasonCode, string correlationId, string? causationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = TenantDomainAuditEventBuilder.Build(
            actor, action, new AuditTarget("tenant_domain", hostname), AuditOutcome.Allowed, reasonCode, correlationId, causationId, detail);

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
            throw TenantDomainError.AuditUnavailable(); // total outage: cannot even verify
        }

        if (!exists)
        {
            throw TenantDomainError.AuditUnavailable(); // confirmed: genuinely never persisted
        }

        // else: confirmed durably present despite the throw - proceed as a success.
    }
}
