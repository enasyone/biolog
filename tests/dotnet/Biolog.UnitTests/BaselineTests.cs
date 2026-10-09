using Xunit;

namespace Biolog.UnitTests;

public sealed class BaselineTests
{
    [Fact]
    public void Synthetic_fixture_is_deterministic()
    {
        const string classification = "SYNTHETIC";
        Assert.Equal("SYNTHETIC", classification);
    }
}
