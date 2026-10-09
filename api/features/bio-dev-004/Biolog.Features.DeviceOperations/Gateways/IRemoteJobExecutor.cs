namespace Biolog.Features.DeviceOperations;

/// <summary>Executes an already-gated, already-audited remote job (BIO-DEV-004-AC02/AC03).
/// Unlike every other gateway in this project, this one needs no cross-project type at all
/// (no vendor SDK exposes a real "reboot"/"push config"/"update firmware" call anywhere in
/// this codebase - disclosed explicitly in docs/features/bio-dev-004/README.md's own "What
/// executing a job actually does" section), so its default implementation
/// (<see cref="SimulatedRemoteJobExecutor"/>) lives directly in this project and is
/// registered by <c>BioDev004Module</c> with no translation needed.</summary>
public interface IRemoteJobExecutor
{
    Task<(RemoteJobOutcome Outcome, string? Detail)> ExecuteAsync(
        DeviceSnapshot device, RemoteJobType jobType, CancellationToken cancellationToken);
}

/// <summary>Deterministic, always-succeeds simulation - this story's own point is the
/// gating/audit discipline around a remote job, not simulating real vendor command
/// failures. A test double (<c>FakeRemoteJobExecutor</c>, test-project-only) substitutes a
/// configurable outcome for the dead-letter/failure-path tests this type itself cannot
/// produce.</summary>
internal sealed class SimulatedRemoteJobExecutor : IRemoteJobExecutor
{
    public Task<(RemoteJobOutcome Outcome, string? Detail)> ExecuteAsync(
        DeviceSnapshot device, RemoteJobType jobType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<(RemoteJobOutcome, string?)>(
            (RemoteJobOutcome.Succeeded, $"{jobType} completed (simulated)."));
    }
}
