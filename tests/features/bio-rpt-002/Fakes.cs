// Developer test fakes for BIO-RPT-002. SYNTHETIC fixtures only.
using Biolog.Features.ReportBuilder.Domain;
using Biolog.Features.ReportBuilder.Tenancy;

namespace Biolog.Features.ReportBuilder.Tests;

public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>A plain in-memory double - fetches whatever <see cref="Seed"/> put in for that tenant, no gating.</summary>
public sealed class FakeCardProductionGateway : ICardProductionGateway
{
    private readonly Dictionary<string, List<CardProductionRecord>> _byTenant = new(StringComparer.Ordinal);

    public void Seed(string tenantId, params CardProductionRecord[] records) =>
        _byTenant[tenantId] = [.. records];

    public Task<IReadOnlyList<CardProductionRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CardProductionRecord>>(_byTenant.TryGetValue(tenantId, out var records) ? records : []);
}

public sealed class FakeAttendanceGateway : IAttendanceGateway
{
    private readonly Dictionary<string, List<AttendanceRecord>> _byTenant = new(StringComparer.Ordinal);

    public void Seed(string tenantId, params AttendanceRecord[] records) =>
        _byTenant[tenantId] = [.. records];

    public Task<IReadOnlyList<AttendanceRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AttendanceRecord>>(_byTenant.TryGetValue(tenantId, out var records) ? records : []);
}

/// <summary>
/// A controllable double whose <see cref="ListAsync"/> blocks until <see cref="Release"/> is
/// called, awaited with the caller's own <see cref="CancellationToken"/>
/// (<c>Task.WaitAsync(cancellationToken)</c>) - so a test can cancel the token BEFORE releasing
/// it and deterministically observe an <see cref="OperationCanceledException"/> propagate, with
/// zero real-time sleeps and zero flakiness (BIO-RPT-002-AC04's own cancellation proof).
/// </summary>
public sealed class GatedCardProductionGateway : ICardProductionGateway
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReadOnlyList<CardProductionRecord> _records = [];

    public void Seed(params CardProductionRecord[] records) => _records = records;

    public void Release() => _gate.TrySetResult();

    public async Task<IReadOnlyList<CardProductionRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        await _gate.Task.WaitAsync(cancellationToken);
        return _records;
    }
}
