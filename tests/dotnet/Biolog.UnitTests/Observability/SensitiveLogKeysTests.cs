using Biolog.Api.Observability;
using Xunit;

namespace Biolog.UnitTests.Observability;

public class SensitiveLogKeysTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("password")]
    [InlineData("UserPassword")]
    [InlineData("Token")]
    [InlineData("AccessToken")]
    [InlineData("ApiKey")]
    [InlineData("api_key")]
    [InlineData("BiometricTemplate")]
    [InlineData("biometric_image")]
    [InlineData("CardKey")]
    [InlineData("SigningKey")]
    [InlineData("PrivateKey")]
    [InlineData("ConnectionString")]
    [InlineData("Credential")]
    [InlineData("Authorization")]
    public void IsSensitive_matches_classified_names(string key)
    {
        Assert.True(SensitiveLogKeys.IsSensitive(key));
    }

    [Theory]
    [InlineData("UserId")]
    [InlineData("CorrelationId")]
    [InlineData("TenantId")]
    [InlineData("DeviceSerial")]
    [InlineData("PolicyId")]
    [InlineData(null)]
    [InlineData("")]
    public void IsSensitive_allows_non_classified_names(string? key)
    {
        Assert.False(SensitiveLogKeys.IsSensitive(key));
    }

    [Fact]
    public void IsSensitive_never_flags_the_framework_template_holder()
    {
        Assert.False(SensitiveLogKeys.IsSensitive("{OriginalFormat}"));
    }
}
