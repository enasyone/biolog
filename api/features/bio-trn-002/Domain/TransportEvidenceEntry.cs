namespace Biolog.Features.TransportEvents.Domain;

/// <summary>One method contributing to AC01's "identity evidence" for a single capture. See <see cref="EvidenceMethod"/>'s own header for why <see cref="TransportEvent.Evidence"/> is a list of these rather than one.</summary>
public sealed record TransportEvidenceEntry
{
    public required EvidenceMethod Method { get; init; }

    public required bool Verified { get; init; }

    /// <summary>0.0-1.0 confidence score from the method (e.g. a face-match score). <c>null</c> when the method does not produce one (e.g. <see cref="EvidenceMethod.Manual"/>) - a genuinely absent measurement, never defaulted to 0 or 1.</summary>
    public double? Confidence { get; init; }

    public static TransportEvidenceEntry Validate(TransportEvidenceEntry entry, int index)
    {
        if (entry.Confidence is < 0.0 or > 1.0)
        {
            throw TransportEventError.Validation([new Api.ValidationErrorItem($"evidence[{index}].confidence", "out_of_range", "confidence must be between 0.0 and 1.0 inclusive")]);
        }

        return entry;
    }
}
