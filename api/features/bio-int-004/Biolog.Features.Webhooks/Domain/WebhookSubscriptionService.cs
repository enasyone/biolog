using System.Security.Cryptography;

namespace Biolog.Features.Webhooks;

public sealed class SubscriptionNotFoundException(string subscriptionId) : Exception($"no webhook subscription '{subscriptionId}' exists for this tenant");

public sealed class SubscriptionConcurrencyConflictException(int currentVersion)
    : Exception($"webhook subscription version conflict - current version is {currentVersion}")
{
    public int CurrentVersion { get; } = currentVersion;
}

/// <summary>
/// BIO-INT-004-AC01 (tenant-scoped, filtered subscriptions) tied to the same
/// actor+provenance+permission gate every sibling story in this codebase uses
/// (ADR-0006 Decision 3 + BIO-IDN-006's real RBAC grants, via <see cref="IPermissionGateway"/>).
/// </summary>
internal sealed class WebhookSubscriptionService(IWebhookSubscriptionRepository repository, IPermissionGateway permissionGateway, bool allowInsecureHttp = false)
{
    public const string ManagePermission = "webhooks.subscription_manage";
    public const string ReadPermission = "webhooks.subscription_read";

    public async Task<WebhookSubscription> CreateAsync(
        string tenantId, AuditActor actor, Uri url, IReadOnlySet<AggregateType> eventTypes, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(tenantId, actor, ManagePermission, cancellationToken);

        // BIO-INT-004-ZB-04 (Zainab, High): HMAC signing proves integrity/authenticity, not
        // transport confidentiality - plain HTTP would let any network observer read an event
        // payload that can carry personal/sensitive data (AC02's own payload_classification), and
        // capture a valid signed request besides. Required outside an explicit, purpose-built
        // opt-in (`allowInsecureHttp`, sourced from `Webhooks:AllowInsecureHttp` configuration,
        // default false/fail-closed) - deliberately NOT an ASPNETCORE_ENVIRONMENT/IsDevelopment()
        // check, the same lesson TD-WEB-PROVENANCE-BRIDGE's own issuance-gate round learned the
        // hard way earlier this session: this app is a demo/showcase product that runs in a real
        // deployed "production" environment by design, so an environment-based gate would either
        // never activate there or have to be disabled for the one environment where HTTP
        // subscriptions are actually wanted for local testing - a purpose-built flag names the
        // real intent directly instead of overloading an environment name.
        if (url.Scheme == Uri.UriSchemeHttp && !allowInsecureHttp)
        {
            throw new ArgumentException("webhook URL must use HTTPS (set Webhooks:AllowInsecureHttp to allow HTTP for local development only)", nameof(url));
        }

        var subscription = new WebhookSubscription(
            subscriptionId: $"whs_{Guid.NewGuid():N}",
            tenantId: tenantId,
            url: url,
            eventTypes: eventTypes,
            enabled: true,
            signingSecretHex: GenerateSigningSecretHex(),
            version: 1,
            createdAt: DateTimeOffset.UtcNow);

        await repository.AddAsync(subscription, cancellationToken);
        return subscription;
    }

    public async Task<IReadOnlyList<WebhookSubscription>> ListAsync(string tenantId, AuditActor actor, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(tenantId, actor, ReadPermission, cancellationToken);
        return await repository.ListForTenantAsync(tenantId, cancellationToken);
    }

    public async Task<WebhookSubscription> SetEnabledAsync(
        string tenantId, AuditActor actor, string subscriptionId, bool enabled, int expectedVersion, CancellationToken cancellationToken)
    {
        await RequirePermissionAsync(tenantId, actor, ManagePermission, cancellationToken);

        var existing = await repository.GetAsync(tenantId, subscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(subscriptionId);
        if (existing.Version != expectedVersion)
        {
            throw new SubscriptionConcurrencyConflictException(existing.Version);
        }

        var updated = existing.WithEnabled(enabled, existing.Version + 1);
        await repository.UpdateAsync(updated, cancellationToken);
        return updated;
    }

    /// <summary>Cryptographically random (BIO-INT-001-F01's own lesson, applied proactively,
    /// the same way BIO-INT-003's own <c>ApiSecretGenerator</c> already does): never derived
    /// from the subscription id or any other value the response also reveals.</summary>
    private static string GenerateSigningSecretHex()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    private async Task RequirePermissionAsync(string tenantId, AuditActor actor, string permissionKey, CancellationToken cancellationToken)
    {
        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, permissionKey, cancellationToken))
        {
            throw new PermissionRequiredException(permissionKey);
        }
    }
}
