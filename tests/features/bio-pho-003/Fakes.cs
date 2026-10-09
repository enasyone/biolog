using Biolog.Features.PhotoQuality.Audit;
using Biolog.Features.PhotoQuality.Domain;
using Biolog.Features.PhotoQuality.Tenancy;

namespace Biolog.Features.PhotoQuality.Tests;

/// <summary>Test double for <see cref="ICurrentTenantProvider"/> - see BIO-IDN-001's identically-named class for the full rationale.</summary>
public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>Test double for <see cref="IPersonExistenceGateway"/>. Defaults to "every person exists".</summary>
public sealed class FakePersonExistenceGateway(IReadOnlySet<string>? existingPersonIds = null) : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(existingPersonIds is null || existingPersonIds.Contains(personId));
}

/// <summary>
/// Test double for <see cref="IPhotoMasterPromotionGateway"/> - records every REAL promotion
/// call without touching a real <c>PhotoMasterService</c>, since this project has no
/// <c>ProjectReference</c> to that one. Kemi's ruling (ZB-PHO003-04): deliberately back to the
/// SAME minimal shape BIO-PHO-002's own fake already uses - identity/crash-recovery is now
/// <see cref="PhotoQualityService"/>'s own local <c>IIdempotencyStore</c> concern, not this
/// gateway's, so this fake no longer needs to model BIO-PHO-001's own audit/repository
/// internals at all. <see cref="FailNextPromotion"/> simulates the external call itself failing
/// (e.g. BIO-PHO-001's own repository commit genuinely failing) - the caller's own idempotency
/// claim is released on any such exception, and a later retry calls this gateway again for real.
/// </summary>
public sealed class FakePhotoMasterPromotionGateway : IPhotoMasterPromotionGateway
{
    private readonly Dictionary<string, List<byte[]>> _versionsByPerson = [];
    public List<(string PersonId, byte[] Content, AuditActor Actor, string CorrelationId)> Promotions { get; } = [];
    public bool FailNextPromotion { get; set; }

    /// <summary>1-indexed version numbers, in promotion order, for the given person.</summary>
    public IReadOnlyList<byte[]> Versions(string personId) => _versionsByPerson.TryGetValue(personId, out var v) ? v : [];

    public Task<int> PromoteAsync(string personId, byte[] content, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        if (FailNextPromotion)
        {
            FailNextPromotion = false;
            throw new InvalidOperationException("simulated external promotion failure (SY-PHO003-07 test fixture)");
        }

        Promotions.Add((personId, content, actor, correlationId));
        var versions = _versionsByPerson.TryGetValue(personId, out var v) ? v : (_versionsByPerson[personId] = []);
        versions.Add(content);
        return Task.FromResult(versions.Count);
    }
}

/// <summary>
/// Test double for <see cref="IBiometricSuitabilityGateway"/> - records every call (so a test can
/// assert this real face-count/liveness check was genuinely consulted) without touching
/// BIO-BIO-003's own <c>IFaceQualityChecker</c>/<c>ILivenessDetector</c>, since this project has
/// no <c>ProjectReference</c> to that one. Defaults to "passes with no reasons"; a test can set
/// <see cref="NextResult"/> to force either independent outcome (BIO-PHO-003-AC01: print and
/// biometric verdicts must be able to diverge in either direction).
/// </summary>
public sealed class FakeBiometricSuitabilityGateway : IBiometricSuitabilityGateway
{
    public BiometricSuitabilityResult NextResult { get; set; } = new(true, []);
    public int CallCount { get; private set; }

    public Task<BiometricSuitabilityResult> AssessAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(NextResult);
    }
}

/// <summary>Test double for <see cref="IPermissionGateway"/>. Defaults to "every permission is granted"; a test can set <see cref="NextResult"/> to force a denial, and reads <see cref="CallCount"/> to prove the gate was genuinely consulted.</summary>
public sealed class FakePermissionGateway : IPermissionGateway
{
    public bool NextResult { get; set; } = true;
    public int CallCount { get; private set; }

    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(NextResult);
    }
}

/// <summary>
/// ZB-PHO003-02: a controllable <see cref="IPhotoQualityAuditSink"/> for proving the
/// promote-then-audit-then-commit recovery path. <see cref="FailWrites"/> simulates a total
/// audit-sink outage - <see cref="WriteAsync"/> throws AND <see cref="ExistsAsync"/> reports
/// false (nothing genuinely persisted), the same "ambiguous write, but verification confirms
/// real failure" shape <see cref="InMemoryPhotoQualityAuditSink"/>'s own consumer
/// (<c>PhotoQualityService.AuditOnceAsync</c>) is built to resolve via <c>ExistsAsync</c> rather
/// than assume. Flipping it back to <c>false</c> mid-test simulates the sink recovering before a
/// retry.
/// </summary>
public sealed class FakeControllableAuditSink : IPhotoQualityAuditSink
{
    private readonly List<PhotoQualityAuditEvent> _events = [];

    public bool FailWrites { get; set; }
    public IReadOnlyList<PhotoQualityAuditEvent> Events => _events;

    public Task WriteAsync(PhotoQualityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        if (FailWrites)
        {
            throw new InvalidOperationException("simulated total audit-sink outage (ZB-PHO003-02 test fixture)");
        }

        _events.Add(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));
}

/// <summary>
/// ZB-PHO003-02 round 2: a controllable <see cref="IPhotoQualityCaptureRepository"/> decorator
/// for proving a crash/repository-failure at a SPECIFIC, numbered <see cref="ReplaceAsync"/>
/// call (1-indexed) - e.g. the marker-commit write itself (call 1 within one
/// <c>ApproveAsync</c> invocation) or the final capture commit after promotion/audit already
/// succeeded (call 2). <see cref="FailOnReplaceCallNumber"/> fails exactly that one call, then
/// behaves normally for every call before and after it.
/// </summary>
internal sealed class FakeControllableCaptureRepository(IPhotoQualityCaptureRepository inner) : IPhotoQualityCaptureRepository
{
    private int _replaceCallCount;
    public int? FailOnReplaceCallNumber { get; set; }

    public Task<PhotoQualityCapture?> GetAsync(string tenantId, string personId, int sequenceNumber, CancellationToken cancellationToken = default) =>
        inner.GetAsync(tenantId, personId, sequenceNumber, cancellationToken);

    public Task<IReadOnlyList<PhotoQualityCapture>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default) =>
        inner.ListForPersonAsync(tenantId, personId, cancellationToken);

    public Task<PhotoQualityCapture> CreateAsync(PhotoQualityCapture capture, CancellationToken cancellationToken = default) =>
        inner.CreateAsync(capture, cancellationToken);

    public Task<PhotoQualityCapture> ReplaceAsync(PhotoQualityCapture expectedCurrent, PhotoQualityCapture updated, CancellationToken cancellationToken = default)
    {
        _replaceCallCount++;
        if (FailOnReplaceCallNumber == _replaceCallCount)
        {
            throw new InvalidOperationException($"simulated repository failure at ReplaceAsync call #{_replaceCallCount} (ZB-PHO003-02 round 2 test fixture)");
        }

        return inner.ReplaceAsync(expectedCurrent, updated, cancellationToken);
    }
}
