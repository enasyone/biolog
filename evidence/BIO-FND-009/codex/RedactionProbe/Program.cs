using Biolog.Api.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

const string biometric = "synthetic-biometric-template-bytes";
const string biometricString = "synthetic-biometric-template-as-generic-string";
const string exceptionSecret = "synthetic-password-in-exception";
const string customStateSecret = "synthetic-biometric-from-custom-formatter";

var state = new List<KeyValuePair<string, object?>>
{
    new("Payload", new BiometricPayload(biometric)),
    new("PayloadText", biometricString),
    new("{OriginalFormat}", "capture received: {Payload}; text: {PayloadText}"),
};
var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
    LogLevel.Error,
    "Independent.RedactionProbe",
    new EventId(1, "Probe"),
    state,
    new InvalidOperationException(exceptionSecret),
    (_, _) => "fallback");

using var output = new StringWriter();
new BiologJsonConsoleFormatter().Write(entry, null, output);
var json = output.ToString();

// ILogger category strings are caller-controlled. An application can request this exact
// category from ILoggerFactory.CreateLogger(string); the prefix is not provenance.
var forgedFrameworkEntry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
    LogLevel.Information,
    "Microsoft.SyntheticApplicationProbe",
    new EventId(2, "ForgedCategory"),
    state,
    null,
    (_, _) => "fallback");
using var forgedOutput = new StringWriter();
new BiologJsonConsoleFormatter().Write(forgedFrameworkEntry, null, forgedOutput);
var forgedJson = forgedOutput.ToString();

// A custom TState has no {OriginalFormat}; Write falls back to the caller's formatter.
var customEntry = new LogEntry<string>(
    LogLevel.Information,
    "Independent.CustomStateProbe",
    new EventId(3, "CustomState"),
    "opaque-state",
    null,
    (_, _) => customStateSecret);
using var customOutput = new StringWriter();
new BiologJsonConsoleFormatter().Write(customEntry, null, customOutput);
var customJson = customOutput.ToString();

var leaks = new List<string>();
if (json.Contains(biometric, StringComparison.Ordinal)) leaks.Add("biometric payload");
if (json.Contains(biometricString, StringComparison.Ordinal)) leaks.Add("generic string payload");
if (json.Contains(exceptionSecret, StringComparison.Ordinal)) leaks.Add("exception secret");
if (forgedJson.Contains(biometricString, StringComparison.Ordinal)) leaks.Add("forged trusted-category string payload");
if (customJson.Contains(customStateSecret, StringComparison.Ordinal)) leaks.Add("custom formatter payload");

if (leaks.Count == 0)
{
    Console.WriteLine("PASS classified payloads are excluded");
    return 0;
}

Console.Error.WriteLine($"FAIL leaked {string.Join(" and ", leaks)}");
return 1;

file sealed record BiometricPayload(string RawTemplate)
{
    public override string ToString() => RawTemplate;
}
