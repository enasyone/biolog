using System.Collections.Concurrent;

namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary keyed by
/// <c>(TenantId, ActionKey, TargetRef, SequenceNumber)</c>. See
/// <see cref="IApprovalRequestRepository"/> for why this class is <c>internal</c> and what
/// belt-and-braces validation it applies independently of <see cref="MakerCheckerService"/>.
/// </summary>
internal sealed class InMemoryApprovalRequestRepository : IApprovalRequestRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string ActionKey, string TargetRef, int SequenceNumber), ApprovalRequest> _requests = new();

    public Task<ApprovalRequest?> GetAsync(string tenantId, string actionKey, string targetRef, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requests.TryGetValue((tenantId, actionKey, targetRef, sequenceNumber), out var request);
        return Task.FromResult(request);
    }

    public Task<IReadOnlyList<ApprovalRequest>> ListHistoryAsync(string tenantId, string actionKey, string targetRef, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ApprovalRequest> items =
        [
            .. _requests.Values
                .Where(r => r.TenantId == tenantId && r.ActionKey == actionKey && r.TargetRef == targetRef)
                .OrderBy(r => r.SequenceNumber),
        ];
        return Task.FromResult(items);
    }

    public Task<ApprovalRequest> CreateAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Status != ApprovalRequestStatus.Pending || request.Version != 1)
        {
            throw RbacError.ApprovalRequestInvalidInitialState(request.ActionKey, request.TargetRef, request.SequenceNumber, request.Status, request.Version);
        }

        // AC03's real structural enforcement point: at most one PENDING request per
        // (tenant, action_key, target_ref) at any time - independently re-checked here, not
        // merely trusted from MakerCheckerService's own pre-lock check.
        var hasPending = _requests.Values.Any(r =>
            r.TenantId == request.TenantId && r.ActionKey == request.ActionKey && r.TargetRef == request.TargetRef && r.Status == ApprovalRequestStatus.Pending);
        if (hasPending)
        {
            throw RbacError.ApprovalRequestAlreadyPending(request.ActionKey, request.TargetRef);
        }

        var key = (request.TenantId, request.ActionKey, request.TargetRef, request.SequenceNumber);
        if (!_requests.TryAdd(key, request))
        {
            // A genuine natural-key collision - never legitimately reachable via
            // MakerCheckerService, which always computes SequenceNumber from this same
            // repository's own history under the per-key lock.
            throw RbacError.ApprovalRequestInvalidInitialState(request.ActionKey, request.TargetRef, request.SequenceNumber, request.Status, request.Version);
        }

        return Task.FromResult(request);
    }

    public Task<ApprovalRequest> ReplaceAsync(ApprovalRequest expected, ApprovalRequest updated, string decidingActorId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (expected.TenantId, expected.ActionKey, expected.TargetRef, expected.SequenceNumber);
        if (!_requests.TryGetValue(key, out var current) || current.TenantId != expected.TenantId)
        {
            throw RbacError.ApprovalRequestNotFound(expected.ActionKey, expected.TargetRef, expected.SequenceNumber);
        }

        if (updated.TenantId != expected.TenantId || updated.ActionKey != expected.ActionKey
            || updated.TargetRef != expected.TargetRef || updated.SequenceNumber != expected.SequenceNumber)
        {
            // Unreachable via `expected with { ... }` (these fields are get-only) - catches two
            // unrelated instances being passed together.
            throw RbacError.ApprovalRequestIdentityImmutable(expected.ActionKey, expected.TargetRef, expected.SequenceNumber);
        }

        if (expected.Status != ApprovalRequestStatus.Pending)
        {
            // Decided requests are terminal - the only governed transition is Pending ->
            // Approved/Rejected. MakerCheckerService.DecideAsync returns early on an
            // already-decided request before ever calling this method, so this is unreachable
            // via any legitimate call and exists purely as the repository-boundary half of the
            // dual-boundary discipline.
            throw RbacError.ApprovalRequestAlreadyDecided(expected.ActionKey, expected.TargetRef, expected.SequenceNumber, expected.Status);
        }

        // Independent re-check of AC03's core security property (separation of duties) at the
        // persistence boundary - not merely trusted from the service's own pre-lock check. A
        // caller cannot bypass this by constructing a `updated` record directly, since
        // RequestedByActorId is compared against the ACTUAL deciding actor passed here, not
        // anything embedded in `updated` itself.
        if (string.Equals(expected.RequestedByActorId, decidingActorId, StringComparison.Ordinal))
        {
            throw RbacError.SelfApprovalForbidden();
        }

        // Structural (record) equality: succeeds only if nothing has changed since the caller
        // read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_requests.TryUpdate(key, updated, expected))
        {
            var latest = _requests.TryGetValue(key, out var r) ? r : current;
            throw RbacError.ApprovalRequestConcurrencyConflict(expected.ActionKey, expected.TargetRef, expected.SequenceNumber, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }

    public Task<ApprovalRequest> ConsumeAsync(ApprovalRequest expected, ApprovalRequest updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (expected.TenantId, expected.ActionKey, expected.TargetRef, expected.SequenceNumber);
        if (!_requests.TryGetValue(key, out var current) || current.TenantId != expected.TenantId)
        {
            throw RbacError.ApprovalRequestNotFound(expected.ActionKey, expected.TargetRef, expected.SequenceNumber);
        }

        if (updated.TenantId != expected.TenantId || updated.ActionKey != expected.ActionKey
            || updated.TargetRef != expected.TargetRef || updated.SequenceNumber != expected.SequenceNumber)
        {
            throw RbacError.ApprovalRequestIdentityImmutable(expected.ActionKey, expected.TargetRef, expected.SequenceNumber);
        }

        // BIO-IDN-006-QA-05's real structural enforcement point: only an APPROVED,
        // not-yet-consumed request may ever be consumed - independently re-checked here, not
        // merely trusted from MakerCheckerService.TryConsumeApprovalAsync's own pre-lock check.
        if (expected.Status != ApprovalRequestStatus.Approved)
        {
            throw RbacError.ApprovalNotApproved(expected.ActionKey, expected.TargetRef, expected.SequenceNumber, expected.Status);
        }

        if (expected.ConsumedAt is not null)
        {
            throw RbacError.ApprovalAlreadyConsumed(expected.ActionKey, expected.TargetRef, expected.SequenceNumber);
        }

        if (!_requests.TryUpdate(key, updated, expected))
        {
            var latest = _requests.TryGetValue(key, out var r) ? r : current;
            throw RbacError.ApprovalRequestConcurrencyConflict(expected.ActionKey, expected.TargetRef, expected.SequenceNumber, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}
