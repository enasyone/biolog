using System.Text.Json.Serialization;

namespace Biolog.Features.DeviceOperations;

/// <summary>The result of attempting a remote job (BIO-DEV-004-AC03/AC04) - never silently
/// dropped; always one of these two, recorded in the audit trail.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RemoteJobOutcome>))]
public enum RemoteJobOutcome
{
    Succeeded,
    Failed,
}
