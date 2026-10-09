using Biolog.Features.PhotoBooth.Api;
using Biolog.Features.PhotoBooth.Domain;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Biolog.Features.PhotoBooth.Tests;

public sealed class DeviceAuthenticatorTests
{
    private static string NewDeviceId() => $"dev_{Guid.CreateVersion7():D}";

    private static HttpRequest RequestWith(string? deviceId, string? secret)
    {
        var context = new DefaultHttpContext();
        if (deviceId is not null) context.Request.Headers[HeaderDeviceAuthenticator.DeviceIdHeaderName] = deviceId;
        if (secret is not null) context.Request.Headers[HeaderDeviceAuthenticator.DeviceSecretHeaderName] = secret;
        return context.Request;
    }

    [Fact]
    public async Task Authenticates_a_device_that_presents_its_own_correct_secret_and_is_active()
    {
        var deviceId = NewDeviceId();
        var store = new InMemoryDeviceCredentialStore();
        store.Register(deviceId, "correct-secret");
        var authenticator = new HeaderDeviceAuthenticator(store, new FakeDeviceExistenceGateway());

        var result = await authenticator.TryAuthenticateAsync(RequestWith(deviceId, "correct-secret"), TestContext.Current.CancellationToken);

        Assert.Equal(deviceId, result);
    }

    [Fact]
    public async Task Rejects_a_request_with_no_headers_at_all()
    {
        var authenticator = new HeaderDeviceAuthenticator(new InMemoryDeviceCredentialStore(), new FakeDeviceExistenceGateway());
        Assert.Null(await authenticator.TryAuthenticateAsync(RequestWith(null, null), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_a_well_formed_device_id_with_the_wrong_secret()
    {
        var deviceId = NewDeviceId();
        var store = new InMemoryDeviceCredentialStore();
        store.Register(deviceId, "correct-secret");
        var authenticator = new HeaderDeviceAuthenticator(store, new FakeDeviceExistenceGateway());

        Assert.Null(await authenticator.TryAuthenticateAsync(RequestWith(deviceId, "wrong-secret"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejects_an_unregistered_device_id_regardless_of_the_secret_presented()
    {
        var authenticator = new HeaderDeviceAuthenticator(new InMemoryDeviceCredentialStore(), new FakeDeviceExistenceGateway());
        Assert.Null(await authenticator.TryAuthenticateAsync(RequestWith(NewDeviceId(), "anything"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BIOPHO002_QA01_a_caller_cannot_claim_a_victim_devices_id_by_pairing_it_with_its_own_valid_secret()
    {
        // The BIO-CRD-001-QA-01 attack, applied against devices from the start: the caller
        // genuinely owns "attacker" device and its real secret, and simply swaps the id header
        // to the victim's. A bare header cannot ever be authority on its own.
        var victimDeviceId = NewDeviceId();
        var attackerDeviceId = NewDeviceId();
        var store = new InMemoryDeviceCredentialStore();
        store.Register(victimDeviceId, "victim-secret");
        store.Register(attackerDeviceId, "attacker-secret");
        var authenticator = new HeaderDeviceAuthenticator(store, new FakeDeviceExistenceGateway());

        var forged = await authenticator.TryAuthenticateAsync(RequestWith(victimDeviceId, "attacker-secret"), TestContext.Current.CancellationToken);

        Assert.Null(forged);
    }

    [Fact]
    public async Task BIOPHO002_AC02_a_retired_device_with_a_correct_secret_still_does_not_authenticate()
    {
        var deviceId = NewDeviceId();
        var store = new InMemoryDeviceCredentialStore();
        store.Register(deviceId, "correct-secret");
        var authenticator = new HeaderDeviceAuthenticator(store, new FakeDeviceExistenceGateway(new HashSet<string>())); // no device is active

        var result = await authenticator.TryAuthenticateAsync(RequestWith(deviceId, "correct-secret"), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-device-id")]
    [InlineData("per_018f2e5a-1234-7abc-89ab-0123456789ab")] // a different type's prefix
    public async Task Rejects_a_malformed_device_id_even_with_a_correct_looking_secret(string deviceId)
    {
        var authenticator = new HeaderDeviceAuthenticator(new InMemoryDeviceCredentialStore(), new FakeDeviceExistenceGateway());
        Assert.Null(await authenticator.TryAuthenticateAsync(RequestWith(deviceId, "anything"), TestContext.Current.CancellationToken));
    }
}
