using Biolog.Features.AccessDecisions.Audit;
using Biolog.Features.AccessDecisions.Domain;
using Biolog.Features.AccessDecisions.Tenancy;

namespace Biolog.Features.AccessDecisions.Tests;

public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>Defaults to "every person exists"; a test that specifically proves the existence check constructs this with <paramref name="existingPersonIds"/> set.</summary>
public sealed class FakePersonExistenceGateway(IReadOnlySet<string>? existingPersonIds = null) : IPersonExistenceGateway
{
    public Task<bool> ExistsAsync(string personId, CancellationToken cancellationToken = default) =>
        Task.FromResult(existingPersonIds is null || existingPersonIds.Contains(personId));
}

/// <summary>Test double for <see cref="IPolicyDecisionGateway"/>. A test scripts a fixed result (or a function of the query, for tests proving the derived assurance level was actually passed through) and asserts on <see cref="LastQuery"/>/<see cref="LastActor"/>.</summary>
public sealed class FakePolicyDecisionGateway(Func<PolicyDecisionQuery, PolicyDecisionResult> respond) : IPolicyDecisionGateway
{
    public PolicyDecisionQuery? LastQuery { get; private set; }
    public AuditActor? LastActor { get; private set; }
    public int CallCount { get; private set; }

    public Task<PolicyDecisionResult> EvaluateAsync(PolicyDecisionQuery query, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastQuery = query;
        LastActor = actor;
        return Task.FromResult(respond(query));
    }
}

/// <summary>Test double for <see cref="IFingerprintEvidenceGateway"/>/<see cref="IFaceEvidenceGateway"/>. Both interfaces share the same shape, so one fake serves either role.</summary>
public sealed class FakeBiometricEvidenceGateway(BiometricEvidenceEntry response) : IFingerprintEvidenceGateway, IFaceEvidenceGateway
{
    public int CallCount { get; private set; }
    public string? LastPersonId { get; private set; }

    public Task<BiometricEvidenceEntry> VerifyAsync(string personId, BiometricSampleInput sample, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastPersonId = personId;
        return Task.FromResult(response);
    }
}
