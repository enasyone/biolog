using System.Collections;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Biolog.Api.Observability;

/// <summary>
/// The local-development structured log view (BIO-FND-009-AC01, AC03): one JSON object per
/// line on stdout, carrying every ambient <c>ILogger.BeginScope</c> value (including the
/// <c>correlation_id</c>/<c>tenant_id</c> pair <see cref="CorrelationLoggingMiddleware"/>
/// pushes) alongside the log's own structured properties. Registered as the
/// <c>Microsoft.Extensions.Logging.Console</c> formatter named "biolog-json" by
/// <see cref="Biolog.Api.Modules.ObservabilityModule"/>, so every provider already wired
/// into the default host (not a second, competing one) emits this shape.
///
/// AC04's default posture, after three review rounds (BIO-FND-009-QA-01, QA-02, QA-03): a
/// value is rendered only when something POSITIVELY establishes it is safe, and the ONLY
/// thing that does is the call site wrapping it in <see cref="SafeLogValue"/> - an explicit
/// assertion that it inspected this exact value and knows it is not classified.
///
/// Two earlier, broader trust sources - the log entry's CATEGORY (trusting anything under
/// <c>Microsoft.</c>/<c>System.</c>/<c>OpenTelemetry.</c>) and a fixed set of scope KEY
/// NAMES (trusting <c>correlation_id</c>/<c>tenant_id</c> and ASP.NET Core's own
/// diagnostics keys) - were removed after Seyi's round-3 review (QA-03): both are labels a
/// caller fully controls, not authenticated identity. Any application code can request
/// <c>ILoggerFactory.CreateLogger("Microsoft.Whatever")</c> or push a
/// <c>BeginScope</c> dictionary containing a key named <c>tenant_id</c> - neither requires
/// being part of .NET's own framework code. <see cref="CorrelationLoggingMiddleware"/>'s
/// own correlation/tenant values now go through the identical <see cref="SafeLogValue"/>
/// opt-in as any other call site; nothing is auto-trusted by name, key, or category
/// anywhere in this formatter.
///
/// The accepted cost is real and was not true before this round: ASP.NET Core's own
/// framework request-diagnostics logging (routing, hosting, health-check details) is now
/// withheld by default too, exactly like application logging, since Microsoft's source
/// cannot be changed to add a <see cref="SafeLogValue"/> wrapper. No acceptance criterion
/// requires that framework-log richness; AC04's "excluded by default" is the one that
/// cannot be compromised for it.
///
/// A name match against <see cref="SensitiveLogKeys"/> still redacts even a
/// <see cref="SafeLogValue"/>-wrapped value: the opt-in covers a value with no name signal,
/// it does not override one that exists.
/// </summary>
public sealed class BiologJsonConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "biolog-json";

    /// <summary>The default-deny marker: no positive safety signal existed for this value,
    /// so it was never rendered at all - distinct from <see cref="SensitiveLogKeys.RedactedValue"/>,
    /// which means the value WAS classified and blocked, a stronger, specific claim this
    /// marker does not make.</summary>
    public const string WithheldValue = "[WITHHELD]";

    /// <summary>
    /// Types safe to render with their own value once a source is already trusted. This is
    /// no longer, by itself, why a value is safe (BIO-FND-009-QA-02: a `string` proved that)
    /// - it only rules out handing an arbitrary caller-defined class's `ToString()` to the
    /// log even from a trusted source, since nothing here needed a foreign type's behaviour
    /// to run.
    /// </summary>
    private static readonly HashSet<Type> SafeScalarTypes =
    [
        typeof(string), typeof(bool), typeof(char),
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
        typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(Guid), typeof(Uri),
    ];

    public BiologJsonConsoleFormatter() : base(FormatterName)
    {
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var properties = logEntry.State as IReadOnlyList<KeyValuePair<string, object?>>;
        var message = RenderMessage(properties) ?? RenderNonStructuredMessage(logEntry.State);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("timestamp", DateTimeOffset.UtcNow.ToString("O"));
            writer.WriteString("level", logEntry.LogLevel.ToString());
            writer.WriteString("category", logEntry.Category);
            if (logEntry.EventId.Id != 0 || !string.IsNullOrEmpty(logEntry.EventId.Name))
            {
                writer.WriteString("eventId", logEntry.EventId.Name ?? logEntry.EventId.Id.ToString());
            }
            writer.WriteString("message", message);

            if (logEntry.Exception is not null)
            {
                writer.WriteString("exception", RenderExceptionSafely(logEntry.Exception));
            }

            WriteProperties(writer, "properties", properties);
            WriteScope(writer, scopeProvider);

            writer.WriteEndObject();
        }

        textWriter.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>
    /// <see cref="Exception.Message"/> is free text the throwing code controls completely -
    /// there is no name to check it against and no type-shape check catches it, since it is
    /// always a <see cref="string"/> (BIO-FND-009-QA-01: the independent review's probe put
    /// a synthetic password directly in an exception message). <see cref="Exception.ToString"/>
    /// includes that message, so it is never called here. The type name and stack trace are
    /// kept - they name code locations, not runtime data, which is what "where did this
    /// break" debugging actually needs, and .NET does not embed local variable values in a
    /// stack trace's text. Accepted cost: a genuinely non-sensitive exception message (the
    /// common case) is also suppressed by default; there is no way to tell the two apart by
    /// inspecting the exception object alone, and the safe default wins.
    /// </summary>
    private static string RenderExceptionSafely(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var stackTrace = current.StackTrace ?? "(no stack trace available)";
            parts.Add($"{current.GetType().FullName}: [message suppressed by default - BIO-FND-009-AC04]\n{stackTrace}");
        }

        return string.Join("\n ---> ", parts);
    }

    /// <summary>
    /// A log entry whose state is not the standard structured KVP list ending in
    /// <c>{OriginalFormat}</c> - e.g. a call that passed a custom <c>TState</c> directly to
    /// <c>ILogger.Log</c> instead of the templated <c>LogInformation</c>/<c>LogError</c>/...
    /// extensions - has no per-argument boundary to check anything against.
    /// <c>logEntry.Formatter</c> is an arbitrary delegate the CALLER supplied; its return
    /// value could be anything, with no name, type, or opt-in signal this formatter could
    /// ever see (BIO-FND-009-QA-04: the independent review's probe returned a synthetic
    /// biometric value from exactly this delegate, bypassing every check in
    /// <see cref="Classify"/> entirely since that path never called it). The delegate is
    /// therefore never invoked: the raw <paramref name="state"/> goes through the identical
    /// <see cref="Classify"/> boundary as any other value instead, which means it renders
    /// only if <paramref name="state"/> is itself a <see cref="SafeLogValue"/>.
    /// </summary>
    private static string RenderNonStructuredMessage(object? state)
    {
        var classification = Classify(key: string.Empty, state);
        return classification.Disposition switch
        {
            Disposition.Scalar => classification.Payload?.ToString() ?? "(null)",
            Disposition.ScalarArray => "[" + string.Join(", ", ((List<object?>)classification.Payload!).Select(item => item?.ToString() ?? "(null)")) + "]",
            Disposition.UnknownType => $"<{classification.Payload}>",
            Disposition.Null => "(null)",
            _ => WithheldValue,
        };
    }

    /// <summary>
    /// Re-renders the message from its template rather than trusting
    /// <c>logEntry.Formatter</c>'s output, which interpolates the ORIGINAL, unredacted
    /// values - the "properties" object alone redacting a value would be pointless if the
    /// same value were still sitting in plain text a few characters over in "message"
    /// (BIO-FND-009-AC04). Returns <c>null</c> when <paramref name="properties"/> is not the
    /// standard structured-logging shape (no trailing <c>{OriginalFormat}</c> entry), in
    /// which case <see cref="RenderNonStructuredMessage"/> handles it instead.
    ///
    /// This does not, and cannot, defend against a caller using C# string interpolation
    /// (<c>$"..."</c>) instead of the structured <c>{Placeholder}</c> template syntax - the
    /// interpolation happens in the CALLER's compiled code before any <c>ILogger</c> method
    /// ever runs, so a classified value interpolated this way arrives already baked into
    /// the literal <c>{OriginalFormat}</c> string with no placeholder, no argument, and
    /// nothing for <see cref="Classify"/> to intercept. This project's <c>.editorconfig</c>
    /// promotes .NET's own CA2254 analyzer ("the logging message template should not vary
    /// between calls", which also fires on an interpolated template) to a build error for
    /// exactly this reason: it is a compile-time control, because no runtime one is
    /// possible here.
    /// </summary>
    private static string? RenderMessage(IReadOnlyList<KeyValuePair<string, object?>>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return null;
        }

        var last = properties[^1];
        if (last.Key != "{OriginalFormat}" || last.Value is not string format)
        {
            return null;
        }

        var args = properties.Count > 1 ? properties.Take(properties.Count - 1).ToList() : [];
        var result = new StringBuilder(format.Length + 32);
        var argIndex = 0;

        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c == '{' && i + 1 < format.Length && format[i + 1] == '{')
            {
                result.Append('{');
                i++;
                continue;
            }
            if (c == '}' && i + 1 < format.Length && format[i + 1] == '}')
            {
                result.Append('}');
                i++;
                continue;
            }
            if (c == '{')
            {
                var close = format.IndexOf('}', i + 1);
                if (close < 0)
                {
                    result.Append(c);
                    continue;
                }
                if (argIndex < args.Count)
                {
                    var (key, value) = args[argIndex];
                    result.Append(RenderForMessage(Classify(key, value)));
                    argIndex++;
                }
                i = close;
                continue;
            }
            result.Append(c);
        }

        return result.ToString();
    }

    private static string RenderForMessage(Classification classification) => classification.Disposition switch
    {
        Disposition.RedactedByName => SensitiveLogKeys.RedactedValue,
        Disposition.WithheldByDefault => WithheldValue,
        Disposition.UnknownType => $"<{classification.Payload}>",
        Disposition.ScalarArray => "[" + string.Join(", ", ((List<object?>)classification.Payload!).Select(item => item?.ToString() ?? "(null)")) + "]",
        _ => classification.Payload?.ToString() ?? "(null)",
    };

    private static void WriteProperties(
        Utf8JsonWriter writer, string propertyName, IReadOnlyList<KeyValuePair<string, object?>>? values)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        writer.WriteStartObject(propertyName);
        foreach (var (key, value) in values)
        {
            if (key == "{OriginalFormat}")
            {
                continue;
            }

            WriteClassified(writer, key, Classify(key, value));
        }
        writer.WriteEndObject();
    }

    private static void WriteScope(Utf8JsonWriter writer, IExternalScopeProvider? scopeProvider)
    {
        if (scopeProvider is null)
        {
            return;
        }

        var scopeValues = new List<KeyValuePair<string, object?>>();
        scopeProvider.ForEachScope(
            (scope, state) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    state.AddRange(pairs);
                }
                else if (scope is not null)
                {
                    state.Add(new KeyValuePair<string, object?>("scope", scope));
                }
            },
            scopeValues);

        if (scopeValues.Count == 0)
        {
            return;
        }

        writer.WriteStartObject("scope");
        foreach (var (key, value) in scopeValues)
        {
            WriteClassified(writer, key, Classify(key, value));
        }
        writer.WriteEndObject();
    }

    private enum Disposition
    {
        RedactedByName,
        WithheldByDefault,
        UnknownType,
        Null,
        Scalar,
        ScalarArray,
    }

    private readonly record struct Classification(Disposition Disposition, object? Payload);

    /// <summary>
    /// The single decision point for every value this formatter ever writes
    /// (BIO-FND-009-AC04, QA-01, QA-02, QA-03): is there a positive reason to trust it, and
    /// if so, is its shape one this formatter knows how to render without running the
    /// value's own (possibly hostile) <c>ToString()</c>. The only positive reason is
    /// <see cref="SafeLogValue"/> - see the type doc for why category and scope-key
    /// name-based trust were removed.
    /// </summary>
    private static Classification Classify(string key, object? value)
    {
        var trusted = false;
        if (value is SafeLogValue safe)
        {
            value = safe.Value;
            trusted = true; // the only source of trust: an explicit call-site assertion
        }

        if (SensitiveLogKeys.IsSensitive(key))
        {
            return new Classification(Disposition.RedactedByName, null);
        }

        if (!trusted)
        {
            return new Classification(Disposition.WithheldByDefault, null);
        }

        if (value is null)
        {
            return new Classification(Disposition.Null, null);
        }

        if (IsSafeScalar(value))
        {
            return new Classification(Disposition.Scalar, value);
        }

        if (TryGetSafeScalarSequence(value, out var items))
        {
            return new Classification(Disposition.ScalarArray, items);
        }

        return new Classification(Disposition.UnknownType, value.GetType().Name);
    }

    private static void WriteClassified(Utf8JsonWriter writer, string key, Classification classification)
    {
        switch (classification.Disposition)
        {
            case Disposition.RedactedByName:
                writer.WriteString(key, SensitiveLogKeys.RedactedValue);
                break;
            case Disposition.WithheldByDefault:
                writer.WriteString(key, WithheldValue);
                break;
            case Disposition.UnknownType:
                writer.WriteString(key, $"<{classification.Payload}>");
                break;
            case Disposition.Null:
                writer.WriteNull(key);
                break;
            case Disposition.Scalar:
                WriteScalar(writer, key, classification.Payload!);
                break;
            case Disposition.ScalarArray:
                writer.WriteStartArray(key);
                foreach (var item in (List<object?>)classification.Payload!)
                {
                    WriteScalar(writer, item);
                }
                writer.WriteEndArray();
                break;
        }
    }

    private static bool IsSafeScalar(object value) => SafeScalarTypes.Contains(value.GetType()) || value.GetType().IsEnum;

    /// <summary>
    /// True only for a non-string <see cref="IEnumerable"/> whose every element is itself a
    /// safe scalar or null - mirrors ADR-0003 Decision 3's "an array of primitives is
    /// copied, anything else is withheld outright" for the same reason: a list of arbitrary
    /// objects is exactly the shape a classified value hides inside one level down.
    /// </summary>
    private static bool TryGetSafeScalarSequence(object value, out List<object?> items)
    {
        items = [];
        if (value is not IEnumerable enumerable || value is string)
        {
            return false;
        }

        foreach (var item in enumerable)
        {
            if (item is not null && !IsSafeScalar(item))
            {
                items.Clear();
                return false;
            }
            items.Add(item);
        }

        return true;
    }

    private static void WriteScalar(Utf8JsonWriter writer, string key, object value)
    {
        switch (value)
        {
            case bool b:
                writer.WriteBoolean(key, b);
                break;
            case sbyte or byte or short or ushort or int or uint or long:
                writer.WriteNumber(key, Convert.ToInt64(value));
                break;
            case ulong ul:
                writer.WriteNumber(key, ul);
                break;
            case float or double or decimal:
                writer.WriteNumber(key, Convert.ToDouble(value));
                break;
            default:
                writer.WriteString(key, value.ToString());
                break;
        }
    }

    private static void WriteScalar(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case sbyte or byte or short or ushort or int or uint or long:
                writer.WriteNumberValue(Convert.ToInt64(value));
                break;
            case ulong ul:
                writer.WriteNumberValue(ul);
                break;
            case float or double or decimal:
                writer.WriteNumberValue(Convert.ToDouble(value));
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }
}
