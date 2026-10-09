using System.Text.Json.Serialization;

namespace Biolog.Features.DeviceAdapters;

/// <summary>
/// The four device kinds BIO-DEV-003's user story names explicitly: "adapter boundaries for
/// fingerprint scanners, face terminals, NFC/UHF readers and card printers." Deliberately a
/// closed, small vocabulary rather than an open string - the same choice
/// <c>Features.DeviceRegistry.DeviceStatus</c> made for the same reason (no shared schema
/// registers device categories yet, and a typed set is what lets <see cref="IDeviceAdapter"/>
/// implementations declare which one they handle without a stringly-typed comparison).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceCategory>))]
public enum DeviceCategory
{
    FingerprintScanner,
    FaceTerminal,
    NfcUhfReader,
    CardPrinter,
}
