using Biolog.Features.Photography.Domain;
using Xunit;

namespace Biolog.Features.Photography.Tests;

/// <summary>BIO-PHO-001-QA-01 (Seyi's independent review): a get-only <c>byte[]</c> property alone does not make <see cref="PhotoMaster"/> immutable - the backing array can still be mutated through either the caller's own retained reference or a previously-read copy.</summary>
public sealed class PhotoMasterTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";

    [Fact]
    public void QA01_mutating_the_array_the_caller_originally_passed_in_never_changes_the_stored_content()
    {
        var content = SyntheticImages.Png(10, 10);
        var originalBytes = (byte[])content.Clone();
        var master = new PhotoMaster(PersonId, TenantId, 1, PhotoFormat.Png, 10, 10, content, "2026-09-22T00:00:00.000Z");

        // The caller still holds `content` and mutates it after construction.
        content[0] = 0x00;
        content[^1] = 0x00;

        Assert.Equal(originalBytes, master.Content);
    }

    [Fact]
    public void QA01_mutating_a_previously_read_copy_never_changes_what_a_later_read_returns()
    {
        var content = SyntheticImages.Png(10, 10);
        var originalBytes = (byte[])content.Clone();
        var master = new PhotoMaster(PersonId, TenantId, 1, PhotoFormat.Png, 10, 10, content, "2026-09-22T00:00:00.000Z");

        var firstRead = master.Content;
        firstRead[0] = 0xFF;
        firstRead[^1] = 0xFF;

        Assert.Equal(originalBytes, master.Content);
        Assert.NotSame(firstRead, master.Content); // each read hands out its own copy
    }
}
