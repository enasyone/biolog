using Biolog.Features.Photography.Domain;
using Xunit;

namespace Biolog.Features.Photography.Tests;

/// <summary>BIO-PHO-001-QA-01 - same property as <see cref="PhotoMasterTests"/>, applied to <see cref="PhotoDerivative"/>.</summary>
public sealed class PhotoDerivativeTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";

    [Fact]
    public void QA01_mutating_the_array_the_caller_originally_passed_in_never_changes_the_stored_content()
    {
        var content = SyntheticImages.Png(10, 10);
        var originalBytes = (byte[])content.Clone();
        var derivative = new PhotoDerivative(
            PersonId, TenantId, 1, DerivativeKind.Print, PhotoFormat.Png, 10, 10, content, "2026-09-22T00:00:00.000Z", "test-generator");

        content[0] = 0x00;
        content[^1] = 0x00;

        Assert.Equal(originalBytes, derivative.Content);
    }

    [Fact]
    public void QA01_mutating_a_previously_read_copy_never_changes_what_a_later_read_returns()
    {
        var content = SyntheticImages.Png(10, 10);
        var originalBytes = (byte[])content.Clone();
        var derivative = new PhotoDerivative(
            PersonId, TenantId, 1, DerivativeKind.Print, PhotoFormat.Png, 10, 10, content, "2026-09-22T00:00:00.000Z", "test-generator");

        var firstRead = derivative.Content;
        firstRead[0] = 0xFF;

        Assert.Equal(originalBytes, derivative.Content);
        Assert.NotSame(firstRead, derivative.Content);
    }
}
