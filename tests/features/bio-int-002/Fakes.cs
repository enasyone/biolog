using Biolog.Features.Integrations.Audit;
using Biolog.Features.Integrations.Domain;
using Biolog.Features.Integrations.Tenancy;

namespace Biolog.Features.Integrations.Tests;

/// <summary>BIO-INT-002-F01's own remediation test hook (Zainab round-1 FAIL): wraps a real <see cref="IImportAuditSink"/> and can make exactly one <see cref="WriteAsync"/> call throw, to reproduce a genuine (never ambiguous - <see cref="ExistsAsync"/> still reports false) audit failure AFTER row processing has already happened.</summary>
public sealed class FaultInjectingImportAuditSink(InMemoryImportAuditSink inner) : IImportAuditSink
{
    public bool ThrowOnNextWrite { get; set; }

    public Task WriteAsync(ImportAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextWrite)
        {
            ThrowOnNextWrite = false;
            throw new InvalidOperationException("injected: import batch audit write failed");
        }

        return inner.WriteAsync(auditEvent, cancellationToken);
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        inner.ExistsAsync(eventId, cancellationToken);

    public IReadOnlyList<ImportAuditEvent> All() => inner.All();
}

public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>Defaults to "every actor holds every permission"; a test that specifically proves the authorization gate constructs this with <paramref name="grantedActorPermissions"/> set.</summary>
public sealed class FakePermissionGateway(IReadOnlySet<(string ActorId, string PermissionKey)>? grantedActorPermissions = null) : IPermissionGateway
{
    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(grantedActorPermissions is null || grantedActorPermissions.Contains((actorId, permissionKey)));
}

/// <summary>A hand-written fake standing in for BIO-IDN-001's own real <c>PersonService</c> - mints a fresh id per registration and tracks created people closely enough to exercise this feature's own import orchestration.</summary>
public sealed class FakePersonGateway : IPersonGateway
{
    private readonly Dictionary<string, PersonView> _people = new(StringComparer.Ordinal);
    private int _nextId = 1;

    public int CreateCallCount { get; private set; }

    /// <summary>BIO-INT-002-F01's own remediation test hook: if set, <see cref="CreateAsync"/> signals
    /// <see cref="CreateEntered"/> (so a test can know deterministically that the claim-owning caller
    /// has actually started executing, not merely claimed) and then awaits this gate before returning
    /// - simulating an owner whose attempt is genuinely still in flight, so a second concurrent caller
    /// can be proven to wait for it (or time out) rather than racing in.</summary>
    public TaskCompletionSource? CreateGate { get; set; }

    public TaskCompletionSource CreateEntered { get; } = new();

    public async Task<PersonView> CreateAsync(CreatePersonFields fields, string actorId, string actorTypeWire, string correlationId, CancellationToken cancellationToken = default)
    {
        CreateCallCount++;
        CreateEntered.TrySetResult();
        if (CreateGate is not null)
        {
            await CreateGate.Task;
        }

        var id = $"per_fake_{_nextId++}";
        var person = new PersonView
        {
            Id = id,
            TenantId = "ten_fake",
            Title = fields.Title,
            FirstName = fields.FirstName,
            MiddleName = fields.MiddleName,
            LastName = fields.LastName,
            PreferredName = fields.PreferredName,
            BornOn = fields.BornOn,
            StatusWire = "PRE_ENROLLED",
        };
        _people[id] = person;
        return person;
    }

    public Task<PersonView?> GetAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_people.TryGetValue(personId, out var p) ? p : null);

    /// <summary>Test seam: seed an already-existing PERSON directly (standing in for one BIO-IDN-001 already has before this story's own import ever runs).</summary>
    public void Seed(PersonView person) => _people[person.Id] = person;
}

/// <summary>BIO-INT-002-F01's own remediation test hook (Zainab round-1 FAIL): wraps a real <see cref="IExternalIdentityLinkStore"/> and can make exactly one <see cref="CreateAsync"/> call throw, to reproduce "PERSON created durably, then the link write for that same row fails" - the exact partial-failure sequence <see cref="IOrphanedPersonLedger"/> exists to recover from.</summary>
internal sealed class FaultInjectingExternalIdentityLinkStore(IExternalIdentityLinkStore inner) : IExternalIdentityLinkStore
{
    public bool ThrowOnNextCreate { get; set; }

    public Task<ExternalIdentityLink?> FindAsync(string tenantId, string mappingId, string externalKey, CancellationToken cancellationToken = default) =>
        inner.FindAsync(tenantId, mappingId, externalKey, cancellationToken);

    public Task CreateAsync(ExternalIdentityLink link, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextCreate)
        {
            ThrowOnNextCreate = false;
            throw new InvalidOperationException("injected: external identity link write failed");
        }

        return inner.CreateAsync(link, cancellationToken);
    }
}

/// <summary>BIO-INT-002-F04's own round-4 remediation test hook (Zainab round-3 FAIL, residual durability note): wraps a real <see cref="IImportRowProvenanceLedger"/> and can make exactly one <see cref="RecordAsync"/> call throw, to prove a genuine (never ambiguous - <see cref="FindAsync"/> still reports nothing recorded) provenance-recording failure now fails the whole attempt instead of being silently swallowed.</summary>
internal sealed class FaultInjectingImportRowProvenanceLedger(IImportRowProvenanceLedger inner) : IImportRowProvenanceLedger
{
    public bool ThrowOnNextRecord { get; set; }

    public Task RecordAsync(string tenantId, string idempotencyKey, string mappingId, string externalKey, ImportRowOutcome outcome, string fingerprint, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextRecord)
        {
            ThrowOnNextRecord = false;
            throw new InvalidOperationException("injected: row provenance write failed");
        }

        return inner.RecordAsync(tenantId, idempotencyKey, mappingId, externalKey, outcome, fingerprint, cancellationToken);
    }

    public Task<ImportRowProvenanceEntry?> FindAsync(string tenantId, string idempotencyKey, string mappingId, string externalKey, CancellationToken cancellationToken = default) =>
        inner.FindAsync(tenantId, idempotencyKey, mappingId, externalKey, cancellationToken);
}

/// <summary>
/// BIO-INT-002-F02's own round-2 remediation test hook (Zainab round-2 FAIL, finding C): lets a test
/// deterministically hold ApplyAsync's early (fail-fast-only) mapping read suspended on its ALREADY-
/// FETCHED value, right at the exact boundary a concurrent <see cref="ImportMappingService.UpdateAsync"/>
/// disable can commit - without racing on timing. Only the FIRST <see cref="GetAsync"/> call for the
/// lifetime of this instance is gateable; every later call (including ApplyAsync's own authoritative
/// post-lock re-read) passes through immediately and always returns the freshest value from
/// <paramref name="inner"/>.
/// </summary>
internal sealed class GateableImportSourceMappingRepository(IImportSourceMappingRepository inner) : IImportSourceMappingRepository
{
    private bool _firstReadDone;

    public TaskCompletionSource? GateFirstRead { get; set; }

    public TaskCompletionSource FirstReadEntered { get; } = new();

    public Task<ImportSourceMapping> CreateAsync(ImportSourceMapping mapping, CancellationToken cancellationToken = default) =>
        inner.CreateAsync(mapping, cancellationToken);

    public async Task<ImportSourceMapping?> GetAsync(string tenantId, string mappingId, CancellationToken cancellationToken = default)
    {
        var result = await inner.GetAsync(tenantId, mappingId, cancellationToken);
        if (!_firstReadDone)
        {
            _firstReadDone = true;
            FirstReadEntered.TrySetResult();
            if (GateFirstRead is not null)
            {
                await GateFirstRead.Task;
            }
        }

        return result;
    }

    public Task<IReadOnlyList<ImportSourceMapping>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        inner.ListByTenantAsync(tenantId, cancellationToken);

    public Task<ImportSourceMapping> ReplaceAsync(ImportSourceMapping expectedCurrent, ImportSourceMapping updated, CancellationToken cancellationToken = default) =>
        inner.ReplaceAsync(expectedCurrent, updated, cancellationToken);
}
