using System.Text.Json.Serialization;

namespace Biolog.Features.DeviceAdapters;

/// <summary>
/// The vocabulary <see cref="DeviceCapabilities.Operations"/> draws from - what a given
/// device category can be asked to do, not how. Deliberately scoped to discovery only
/// (BIO-DEV-003-AC02: "device capability discovery ... are supported"), not the operations
/// themselves - actually capturing a fingerprint, matching a face or printing a card is
/// already-scoped territory the BIO-BIO-*/BIO-CRD-* epics own; this framework only reports
/// which of those a given vendor/model/firmware/sdk combination declares it supports, so a
/// caller can decide whether to attempt one at all.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceOperation>))]
public enum DeviceOperation
{
    CaptureFingerprint,
    VerifyFingerprint,
    DetectFace,
    MatchFace,
    LivenessCheck,
    ReadTag,
    WriteTag,
    EncodeTag,
    PrintCard,
    EncodeCard,
}
