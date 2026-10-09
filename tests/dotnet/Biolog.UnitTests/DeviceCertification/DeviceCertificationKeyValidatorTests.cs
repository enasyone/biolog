using Biolog.Api.Features.DeviceCertification;
using Xunit;

namespace Biolog.UnitTests.DeviceCertification;

public class DeviceCertificationKeyValidatorTests
{
    private static DeviceCertificationKey SampleKey() => new("Acme", "ReaderX", "1.2.3", "sdk-2.0");

    [Fact]
    public void Validate_allows_a_well_formed_key()
    {
        DeviceCertificationKeyValidator.Validate(SampleKey());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_rejects_an_empty_or_whitespace_field(string blank)
    {
        Assert.Throws<InvalidDeviceCertificationKeyException>(
            () => DeviceCertificationKeyValidator.Validate(SampleKey() with { Vendor = blank }));
    }

    [Theory]
    [InlineData("Acme/Sub")] // '/' - the composite-key separator
    [InlineData("Acme:Sub")] // ':' - the tenant-scope separator
    public void Validate_rejects_a_field_containing_a_reserved_separator(string value)
    {
        // The exact defect this validator exists to prevent: two different combinations
        // colliding onto the same composite string. Vendor "Acme/Sub", Model "X" would
        // otherwise produce the identical composite as Vendor "Acme", Model "Sub/X".
        Assert.Throws<InvalidDeviceCertificationKeyException>(
            () => DeviceCertificationKeyValidator.Validate(SampleKey() with { Vendor = value }));
    }

    [Fact]
    public void Validate_rejects_a_control_character()
    {
        Assert.Throws<InvalidDeviceCertificationKeyException>(
            () => DeviceCertificationKeyValidator.Validate(SampleKey() with { Model = "Reader\u0000X" }));
    }

    [Fact]
    public void Validate_rejects_a_field_exceeding_the_maximum_length()
    {
        var tooLong = new string('a', 129);

        Assert.Throws<InvalidDeviceCertificationKeyException>(
            () => DeviceCertificationKeyValidator.Validate(SampleKey() with { Sdk = tooLong }));
    }

    [Fact]
    public void Composite_joins_every_field_with_a_slash()
    {
        Assert.Equal("Acme/ReaderX/1.2.3/sdk-2.0", SampleKey().Composite);
    }

    [Fact]
    public void Different_vendor_model_splits_do_not_collide_once_the_separator_is_rejected()
    {
        // Demonstrates the collision the validator prevents: without it, ("Acme/Sub", "X")
        // and ("Acme", "Sub/X") would both compute to "Acme/Sub/X/..." for the same
        // firmware/sdk. With validation in place, constructing either directly throws
        // before a composite is ever computed from it.
        var collidingA = new DeviceCertificationKey("Acme/Sub", "X", "1.0", "sdk-1");
        var collidingB = new DeviceCertificationKey("Acme", "Sub/X", "1.0", "sdk-1");

        Assert.Throws<InvalidDeviceCertificationKeyException>(() => DeviceCertificationKeyValidator.Validate(collidingA));
        Assert.Throws<InvalidDeviceCertificationKeyException>(() => DeviceCertificationKeyValidator.Validate(collidingB));
    }
}
