using System.Text;
using Biolog.Features.Biometrics.Providers;

namespace Biolog.Features.Liveness.Domain;

/// <summary>
/// BIO-BIO-008-AC04: "Replay/screen/photo adversarial cases are represented in test interfaces."
/// This IS that test interface - a named factory producing a deterministic, tagged
/// <see cref="BiometricSample"/> per <see cref="AdversarialCaseKind"/> that
/// <see cref="SyntheticLivenessProvider"/> recognises and answers with
/// <see cref="LivenessStatus.SpoofDetected"/>. No real presentation-attack detection exists
/// anywhere in this repo (same constraint as every other synthetic provider here) - the tag is a
/// plain byte-string marker prefixed to the payload, not an inferred signal.
/// </summary>
internal static class AdversarialSampleFactory
{
    private static readonly IReadOnlyDictionary<AdversarialCaseKind, byte[]> Markers =
        new Dictionary<AdversarialCaseKind, byte[]>
        {
            [AdversarialCaseKind.Replay] = Encoding.UTF8.GetBytes("BIOLOG-LIVENESS-ADVERSARIAL:replay:"),
            [AdversarialCaseKind.ScreenPresentation] = Encoding.UTF8.GetBytes("BIOLOG-LIVENESS-ADVERSARIAL:screen:"),
            [AdversarialCaseKind.PhotoPresentation] = Encoding.UTF8.GetBytes("BIOLOG-LIVENESS-ADVERSARIAL:photo:"),
        };

    /// <summary>A tagged sample for the given adversarial case - <see cref="SyntheticLivenessProvider"/>
    /// always answers <see cref="LivenessStatus.SpoofDetected"/> for one of these.</summary>
    public static BiometricSample Create(AdversarialCaseKind kind, string capturedAtUtc = "2026-01-01T00:00:00Z")
    {
        var marker = Markers[kind];
        var payload = new byte[marker.Length + 8];
        marker.CopyTo(payload, 0);
        // Trailing filler bytes only - never real biometric data (this story's own
        // "synthetic data only" constraint), just enough to be a non-empty payload.
        for (var i = marker.Length; i < payload.Length; i++)
        {
            payload[i] = (byte)(i - marker.Length);
        }

        return new BiometricSample { Payload = payload, ContentType = "application/octet-stream", CapturedAtUtc = capturedAtUtc };
    }

    /// <summary>A genuine (non-adversarial) synthetic sample - <see cref="SyntheticLivenessProvider"/>
    /// evaluates this deterministically by content, never as a forced SpoofDetected.</summary>
    public static BiometricSample Genuine(byte[] seed, string capturedAtUtc = "2026-01-01T00:00:00Z") =>
        new() { Payload = seed, ContentType = "application/octet-stream", CapturedAtUtc = capturedAtUtc };

    internal static bool TryRecognise(byte[] payload, out AdversarialCaseKind kind)
    {
        foreach (var (candidateKind, marker) in Markers)
        {
            if (payload.Length >= marker.Length && payload.AsSpan(0, marker.Length).SequenceEqual(marker))
            {
                kind = candidateKind;
                return true;
            }
        }

        kind = default;
        return false;
    }
}
