using Biolog.Features.FingerprintVerification.Domain;
using Xunit;

namespace Biolog.Features.FingerprintVerification.Tests;

public sealed class IdentityStatusTests
{
    [Theory]
    [InlineData(IdentityStatus.PreEnrolled, "PRE_ENROLLED")]
    [InlineData(IdentityStatus.Verified, "VERIFIED")]
    [InlineData(IdentityStatus.Active, "ACTIVE")]
    [InlineData(IdentityStatus.Suspended, "SUSPENDED")]
    [InlineData(IdentityStatus.Expired, "EXPIRED")]
    [InlineData(IdentityStatus.FormerAlumni, "FORMER_ALUMNI")]
    [InlineData(IdentityStatus.Revoked, "REVOKED")]
    [InlineData(IdentityStatus.Archived, "ARCHIVED")]
    public void ToWireString_and_TryParse_round_trip_every_state(IdentityStatus status, string wire)
    {
        Assert.Equal(wire, status.ToWireString());
        Assert.True(IdentityStatusExtensions.TryParse(wire, out var parsed));
        Assert.Equal(status, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NOT_A_REAL_STATUS")]
    public void TryParse_rejects_unrecognised_wire_strings(string? wire)
    {
        Assert.False(IdentityStatusExtensions.TryParse(wire, out _));
    }
}
