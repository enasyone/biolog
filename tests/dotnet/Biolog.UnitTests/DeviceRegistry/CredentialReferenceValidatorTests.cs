using Biolog.Api.Features.DeviceRegistry;
using Xunit;

namespace Biolog.UnitTests.DeviceRegistry;

public class CredentialReferenceValidatorTests
{
    [Fact]
    public void Validate_allows_a_null_reference()
    {
        // AC02 leaves the credential reference optional - a device can be registered
        // before its credential is provisioned.
        CredentialReferenceValidator.Validate(null);
    }

    [Theory]
    [InlineData("vault://devices/dev-001/cred")]
    [InlineData("kv://synthetic/reader-01")]
    [InlineData("kms://key-alias/dev-001")]
    [InlineData("secretref://ref-1")]
    public void Validate_allows_a_supported_reference(string reference)
    {
        CredentialReferenceValidator.Validate(reference);
    }

    [Theory]
    [InlineData("synthetic-plaintext-password")]
    [InlineData("hunter2")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("vault://svc-account@vault.internal/reader-01")] // credential-bearing URL shape (embedded userinfo)
    [InlineData("https://vault.internal/reader-01")] // unsupported scheme
    [InlineData("ftp://vault.internal/reader-01")] // unsupported scheme
    [InlineData("vault://has a space")]
    [InlineData("vault://has\ta\tcontrol\tchar")]
    public void Validate_rejects_plaintext_and_malformed_references(string reference)
    {
        var ex = Assert.Throws<InvalidCredentialReferenceException>(() => CredentialReferenceValidator.Validate(reference));
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void Validate_rejects_a_control_character()
    {
        var reference = "vault://reader-01\u0000";

        Assert.Throws<InvalidCredentialReferenceException>(() => CredentialReferenceValidator.Validate(reference));
    }
}
