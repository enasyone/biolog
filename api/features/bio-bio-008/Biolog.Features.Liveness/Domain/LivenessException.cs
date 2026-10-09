namespace Biolog.Features.Liveness.Domain;

/// <summary>
/// Genuine caller/integration errors only (BIO-BIO-004-F01's own typed-outcome-vs-exception
/// discipline, applied here). A liveness check that reaches a real, meaningful verdict - even a
/// negative one like <see cref="LivenessStatus.SpoofDetected"/> or
/// <see cref="LivenessStatus.Inconclusive"/> - is a routine <see cref="LivenessEvidence"/>, never
/// an exception. Only a request that cannot be evaluated at all throws.
/// </summary>
public sealed class LivenessException : InvalidOperationException
{
    private LivenessException(string message) : base(message)
    {
    }

    public static LivenessException EmptySample() =>
        new("liveness_sample_empty: cannot evaluate liveness against an empty payload.");
}
