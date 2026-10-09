using Xunit;

namespace Biolog.IntegrationTests;

public sealed class BaselineIntegrationTests
{
    [Fact]
    public void Harness_starts_without_external_services()
    {
        Assert.True(true);
    }
}
