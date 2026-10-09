using Biolog.Api.Identifiers;
using Xunit;

namespace Biolog.UnitTests.Identifiers;

public class CanonicalIdTests
{
    [Fact]
    public void NewDeviceId_produces_a_dev_prefixed_uuidv7()
    {
        var id = CanonicalId.NewDeviceId();

        Assert.StartsWith("dev_", id);
        Assert.True(CanonicalId.IsWellFormed(id, "dev"));
    }

    [Fact]
    public void NewEventId_produces_an_evt_prefixed_uuidv7()
    {
        var id = CanonicalId.NewEventId();

        Assert.StartsWith("evt_", id);
        Assert.True(CanonicalId.IsWellFormed(id, "evt"));
    }

    [Fact]
    public void NewDeviceId_values_are_unique()
    {
        var first = CanonicalId.NewDeviceId();
        var second = CanonicalId.NewDeviceId();

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dev_not-a-uuid")]
    [InlineData("dev_0199e0c1-8b2f-7c31-9a4e")] // truncated
    public void IsWellFormed_rejects_malformed_values(string? value)
    {
        Assert.False(CanonicalId.IsWellFormed(value, "dev"));
    }

    [Fact]
    public void IsWellFormed_rejects_a_wellformed_uuid_under_the_wrong_prefix()
    {
        // ADR-0001 §1.4: catching "a device id was passed where an event id was expected"
        // at the boundary is the whole point of a typed identifier.
        var deviceId = CanonicalId.NewDeviceId();

        Assert.False(CanonicalId.IsWellFormed(deviceId, "evt"));
    }
}
