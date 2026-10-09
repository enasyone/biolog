using System.Collections.Concurrent;

namespace Biolog.Features.OperationalDashboard.Domain;

/// <summary>
/// Seyi round-2, BIO-RPT-004-F04 (High): round 2's own fault-isolation fix caught the exception
/// but then fabricated a zero-valued <see cref="DataFreshness.Stale"/> panel stamped with the
/// failure's own timestamp - indistinguishable, at a glance, from "this tenant genuinely has zero
/// people/decisions/jobs right now." A process-lifetime, in-memory record of the last
/// successfully-observed counts per tenant per domain, so a failed read can report what was last
/// actually seen (and when) instead of inventing a fresh zero. <see cref="DashboardService"/>
/// records into this on every successful read and consults it on every failed one - when nothing
/// has ever succeeded for a tenant, the panel reports no counts at all (<c>null</c>), never zero.
///
/// Singleton (process lifetime, not request-scoped) - the same "must survive past any one HTTP
/// request's own scope" reasoning BIO-RPT-002's own <c>ReportJobRunner</c> singleton already
/// documents, adapted here to a read-side cache rather than a write-side job registry.
/// </summary>
public sealed class LastKnownHealthCache
{
    private readonly ConcurrentDictionary<string, (IdentityCounts Counts, string ObservedAt)> _identity = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (AccessCounts Counts, string ObservedAt)> _access = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (ProductionCounts Counts, string ObservedAt)> _production = new(StringComparer.Ordinal);

    public void RecordIdentity(string tenantId, IdentityCounts counts, string observedAt) => _identity[tenantId] = (counts, observedAt);
    public (IdentityCounts Counts, string ObservedAt)? GetIdentity(string tenantId) => _identity.TryGetValue(tenantId, out var value) ? value : null;

    public void RecordAccess(string tenantId, AccessCounts counts, string observedAt) => _access[tenantId] = (counts, observedAt);
    public (AccessCounts Counts, string ObservedAt)? GetAccess(string tenantId) => _access.TryGetValue(tenantId, out var value) ? value : null;

    public void RecordProduction(string tenantId, ProductionCounts counts, string observedAt) => _production[tenantId] = (counts, observedAt);
    public (ProductionCounts Counts, string ObservedAt)? GetProduction(string tenantId) => _production.TryGetValue(tenantId, out var value) ? value : null;
}
