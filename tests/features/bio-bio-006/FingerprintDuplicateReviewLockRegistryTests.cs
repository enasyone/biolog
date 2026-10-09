using Biolog.Features.FingerprintSearch.Domain;
using Xunit;

namespace Biolog.Features.FingerprintSearch.Tests;

/// <summary>BIO-BIO-006-QA-02 (Chioma acceptance, High): direct, deterministic proof of the
/// exact mechanism that closes the race - a second acquire for the SAME pair genuinely waits
/// until the first is released, while a different pair is never blocked by it.</summary>
public sealed class FingerprintDuplicateReviewLockRegistryTests
{
    [Fact]
    public async Task A_second_acquire_for_the_same_pair_waits_until_the_first_is_released()
    {
        var registry = new FingerprintDuplicateReviewLockRegistry();
        var first = await registry.AcquireAsync("ten_a", "per_a", "per_b", CancellationToken.None);

        var secondAcquireTask = registry.AcquireAsync("ten_a", "per_a", "per_b", CancellationToken.None);

        // Give the second acquire a fair chance to (wrongly) complete immediately.
        var completedEarly = await Task.WhenAny(secondAcquireTask, Task.Delay(50, TestContext.Current.CancellationToken)) == secondAcquireTask;
        Assert.False(completedEarly);

        first.Dispose();

        var second = await secondAcquireTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.NotNull(second);
        second.Dispose();
    }

    [Fact]
    public async Task Acquiring_a_different_pair_never_waits_on_an_unrelated_pairs_lock()
    {
        var registry = new FingerprintDuplicateReviewLockRegistry();
        using var _ = await registry.AcquireAsync("ten_a", "per_a", "per_b", CancellationToken.None);

        var unrelated = await registry.AcquireAsync("ten_a", "per_c", "per_d", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.NotNull(unrelated);
        unrelated.Dispose();
    }
}
