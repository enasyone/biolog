using System.Text.Json;
using Biolog.Api.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Biolog.UnitTests.Observability;

public class BiologJsonConsoleFormatterTests
{
    private const string OrdinaryCategory = "Biolog.UnitTests.TestCategory";

    private sealed class FakeScopeProvider : IExternalScopeProvider
    {
        private readonly object?[] _scopes;
        public FakeScopeProvider(params object?[] scopes) => _scopes = scopes;

        public void ForEachScope<TState>(Action<object?, TState> callback, TState state)
        {
            foreach (var scope in _scopes)
            {
                callback(scope, state);
            }
        }

        public IDisposable Push(object? state) => throw new NotSupportedException("Not needed by these tests.");
    }

    private static string WriteAndCapture(
        IReadOnlyList<KeyValuePair<string, object?>> state,
        string fallbackMessage,
        IExternalScopeProvider? scopeProvider = null,
        Exception? exception = null,
        string category = OrdinaryCategory)
    {
        var formatter = new BiologJsonConsoleFormatter();
        var entry = new LogEntry<IReadOnlyList<KeyValuePair<string, object?>>>(
            LogLevel.Information,
            category,
            new EventId(7, "TestEvent"),
            state,
            exception,
            (_, _) => fallbackMessage);

        using var writer = new StringWriter();
        formatter.Write(entry, scopeProvider, writer);
        return writer.ToString();
    }

    [Fact]
    public void Write_emits_correlation_and_tenant_from_scope_when_explicitly_marked_safe()
    {
        // Mirrors CorrelationLoggingMiddleware, which wraps both values in SafeLogValue
        // itself (BIO-FND-009-QA-03) - nothing renders here by virtue of the key names alone.
        var state = new List<KeyValuePair<string, object?>> { new("{OriginalFormat}", "request handled") };
        var scope = new FakeScopeProvider(new Dictionary<string, object?>
        {
            ["correlation_id"] = SafeLogValue.Of("00-abc-def-01"),
            ["tenant_id"] = SafeLogValue.Of(null),
        });

        var json = WriteAndCapture(state, "request handled", scope);
        var doc = JsonDocument.Parse(json);

        var scopeElement = doc.RootElement.GetProperty("scope");
        Assert.Equal("00-abc-def-01", scopeElement.GetProperty("correlation_id").GetString());
        Assert.Equal(JsonValueKind.Null, scopeElement.GetProperty("tenant_id").ValueKind);
    }

    [Fact]
    public void Write_withholds_correlation_and_tenant_scope_values_that_are_not_wrapped()
    {
        // Regression for BIO-FND-009-QA-03: a scope key named correlation_id/tenant_id is
        // not, by itself, proof of where the value came from - any caller can push the same
        // key. Only an actual SafeLogValue wrapper establishes trust now.
        var state = new List<KeyValuePair<string, object?>> { new("{OriginalFormat}", "request handled") };
        var scope = new FakeScopeProvider(new Dictionary<string, object?>
        {
            ["correlation_id"] = "forged-by-a-caller-not-the-middleware",
            ["tenant_id"] = "synthetic-classified-tenant-payload",
        });

        var json = WriteAndCapture(state, "request handled", scope);

        Assert.DoesNotContain("forged-by-a-caller-not-the-middleware", json);
        Assert.DoesNotContain("synthetic-classified-tenant-payload", json);
    }

    [Fact]
    public void Write_redacts_sensitive_property_by_name_even_when_the_call_site_marks_it_safe()
    {
        var state = new List<KeyValuePair<string, object?>>
        {
            new("UserId", SafeLogValue.Of("u_123")),
            new("Password", SafeLogValue.Of("hunter2")),
            new("{OriginalFormat}", "User {UserId} authenticated with {Password}"),
        };

        var json = WriteAndCapture(state, "User u_123 authenticated with hunter2");
        var doc = JsonDocument.Parse(json);
        var properties = doc.RootElement.GetProperty("properties");

        Assert.Equal("u_123", properties.GetProperty("UserId").GetString());
        Assert.Equal(SensitiveLogKeys.RedactedValue, properties.GetProperty("Password").GetString());
        Assert.DoesNotContain("hunter2", json);
    }

    [Fact]
    public void Write_redacts_sensitive_value_inside_the_rendered_message_itself()
    {
        // Regression: redacting only the "properties" object is worthless if the same
        // secret value is still sitting in plain text in "message" a few characters over.
        var state = new List<KeyValuePair<string, object?>>
        {
            new("UserId", SafeLogValue.Of("u_123")),
            new("Password", SafeLogValue.Of("hunter2")),
            new("{OriginalFormat}", "User {UserId} authenticated with {Password}"),
        };

        var json = WriteAndCapture(state, "User u_123 authenticated with hunter2");
        var doc = JsonDocument.Parse(json);

        Assert.Equal(
            $"User u_123 authenticated with {SensitiveLogKeys.RedactedValue}",
            doc.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("hunter2", json);
    }

    [Fact]
    public void Write_redacts_biometric_named_scope_values()
    {
        var state = new List<KeyValuePair<string, object?>> { new("{OriginalFormat}", "capture processed") };
        var scope = new FakeScopeProvider(new Dictionary<string, object?>
        {
            ["biometric_template"] = "raw-template-bytes-as-string",
        });

        var json = WriteAndCapture(state, "capture processed", scope);

        Assert.DoesNotContain("raw-template-bytes-as-string", json);
        Assert.Contains(SensitiveLogKeys.RedactedValue, json);
    }

    [Fact]
    public void Write_withholds_the_message_for_non_structured_custom_state_by_default()
    {
        // Regression for BIO-FND-009-QA-04: a log call that hands ILogger.Log a custom
        // TState directly (no {OriginalFormat}) previously fell back to logEntry.Formatter -
        // an arbitrary caller-supplied delegate whose return value bypassed Classify
        // entirely. The independent review's probe returned a synthetic biometric value
        // from exactly this delegate.
        const string secretValue = "synthetic-biometric-from-custom-formatter";
        var formatter = new BiologJsonConsoleFormatter();
        var entry = new LogEntry<string>(
            LogLevel.Information,
            OrdinaryCategory,
            new EventId(9, "CustomState"),
            "opaque-state",
            null,
            (_, _) => secretValue);

        using var writer = new StringWriter();
        formatter.Write(entry, scopeProvider: null, writer);
        var json = writer.ToString();
        var doc = JsonDocument.Parse(json);

        Assert.DoesNotContain(secretValue, json);
        Assert.Equal(BiologJsonConsoleFormatter.WithheldValue, doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Write_never_invokes_the_callers_formatter_for_non_structured_state()
    {
        // Stronger than "the output is ignored": the delegate is never called at all, since
        // it is arbitrary caller code this formatter has no reason to execute.
        var invoked = false;
        var formatter = new BiologJsonConsoleFormatter();
        var entry = new LogEntry<string>(
            LogLevel.Information,
            OrdinaryCategory,
            new EventId(9, "CustomState"),
            "opaque-state",
            null,
            (_, _) =>
            {
                invoked = true;
                return "should never run";
            });

        using var writer = new StringWriter();
        formatter.Write(entry, scopeProvider: null, writer);

        Assert.False(invoked);
    }

    [Fact]
    public void Write_renders_non_structured_state_when_explicitly_marked_safe()
    {
        // The same SafeLogValue opt-in applies symmetrically to non-structured state.
        var formatter = new BiologJsonConsoleFormatter();
        var entry = new LogEntry<SafeLogValue>(
            LogLevel.Information,
            OrdinaryCategory,
            new EventId(9, "CustomState"),
            SafeLogValue.Of("safe custom state"),
            null,
            (_, _) => "should never run");

        using var writer = new StringWriter();
        formatter.Write(entry, scopeProvider: null, writer);
        var doc = JsonDocument.Parse(writer.ToString());

        Assert.Equal("safe custom state", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Write_suppresses_the_exception_message_but_keeps_type_and_stack_trace()
    {
        // Regression for BIO-FND-009-QA-01: an exception's Message is caller-controlled free
        // text with no name to match against SensitiveLogKeys - the independent review's
        // probe put a synthetic password directly there. The type name and stack trace are
        // still useful for "where did this break" and carry no runtime data of their own.
        Exception exception;
        try
        {
            throw new InvalidOperationException("synthetic-password-in-exception");
        }
        catch (InvalidOperationException ex)
        {
            exception = ex;
        }

        var state = new List<KeyValuePair<string, object?>> { new("{OriginalFormat}", "boom") };
        var json = WriteAndCapture(state, "boom", exception: exception);
        var doc = JsonDocument.Parse(json);
        var rendered = doc.RootElement.GetProperty("exception").GetString();

        Assert.DoesNotContain("synthetic-password-in-exception", json);
        Assert.Contains("InvalidOperationException", rendered);
        Assert.Contains(nameof(Write_suppresses_the_exception_message_but_keeps_type_and_stack_trace), rendered);
    }

    [Fact]
    public void Write_withholds_a_classified_object_logged_under_a_generic_property_name_by_default()
    {
        // Regression for BIO-FND-009-QA-01: SensitiveLogKeys only matches property NAMES, so
        // a classified object logged under an unrelated name (e.g. "Payload") previously
        // reached the log verbatim via its own ToString() override.
        const string biometricValue = "synthetic-biometric-template-bytes";
        var payload = new RecordWithCustomToString(biometricValue);
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Payload", payload),
            new("{OriginalFormat}", "capture received: {Payload}"),
        };

        var json = WriteAndCapture(state, $"capture received: {biometricValue}");
        var doc = JsonDocument.Parse(json);

        Assert.DoesNotContain(biometricValue, json);
        Assert.Equal(BiologJsonConsoleFormatter.WithheldValue, doc.RootElement.GetProperty("properties").GetProperty("Payload").GetString());
        Assert.Contains(BiologJsonConsoleFormatter.WithheldValue, doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Write_withholds_a_plain_string_under_a_generic_property_name_by_default()
    {
        // Regression for BIO-FND-009-QA-02: a classified value is, overwhelmingly, just a
        // string - an earlier fix's type allowlist treated every string as safe, which
        // reopened the identical hole one level down.
        const string biometricValue = "synthetic-biometric-template-as-generic-string";
        var state = new List<KeyValuePair<string, object?>>
        {
            new("PayloadText", biometricValue),
            new("{OriginalFormat}", "text: {PayloadText}"),
        };

        var json = WriteAndCapture(state, $"text: {biometricValue}");
        var doc = JsonDocument.Parse(json);

        Assert.DoesNotContain(biometricValue, json);
        Assert.Equal(BiologJsonConsoleFormatter.WithheldValue, doc.RootElement.GetProperty("properties").GetProperty("PayloadText").GetString());
        Assert.Contains(BiologJsonConsoleFormatter.WithheldValue, doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Write_withholds_an_array_of_plain_strings_under_a_generic_property_name_by_default()
    {
        // Same QA-02 hole, one level down again: a list of strings inherited the same
        // "strings are safe" trust the scalar case did.
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Channels", new List<string> { "sms", "email" }),
            new("{OriginalFormat}", "notified via {Channels}"),
        };

        var json = WriteAndCapture(state, "notified via [sms, email]");
        var doc = JsonDocument.Parse(json);

        Assert.Equal(BiologJsonConsoleFormatter.WithheldValue, doc.RootElement.GetProperty("properties").GetProperty("Channels").GetString());
        Assert.DoesNotContain("sms", json);
    }

    [Fact]
    public void Write_renders_a_value_the_call_site_explicitly_marks_safe()
    {
        // The positive, enforceable contract the review required: a call site that has
        // actually verified a value is not classified can say so explicitly.
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Channels", SafeLogValue.Of(new List<string> { "sms", "email" })),
            new("{OriginalFormat}", "notified via {Channels}"),
        };

        var json = WriteAndCapture(state, "notified via [sms, email]");
        var doc = JsonDocument.Parse(json);
        var channels = doc.RootElement.GetProperty("properties").GetProperty("Channels");

        Assert.Equal(JsonValueKind.Array, channels.ValueKind);
        Assert.Equal(["sms", "email"], channels.EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("[sms, email]", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Write_withholds_values_even_under_a_forged_framework_category_name()
    {
        // Regression for BIO-FND-009-QA-03: any application call site can request
        // ILoggerFactory.CreateLogger("Microsoft.Whatever") - a category string is a label
        // the caller chooses, not proof the log statement originates from .NET's own code.
        const string biometricValue = "synthetic-biometric-template-as-generic-string";
        var state = new List<KeyValuePair<string, object?>>
        {
            new("PayloadText", biometricValue),
            new("{OriginalFormat}", "text: {PayloadText}"),
        };

        var json = WriteAndCapture(state, $"text: {biometricValue}", category: "Microsoft.SyntheticApplicationProbe");
        var doc = JsonDocument.Parse(json);

        Assert.DoesNotContain(biometricValue, json);
        Assert.Equal(BiologJsonConsoleFormatter.WithheldValue, doc.RootElement.GetProperty("properties").GetProperty("PayloadText").GetString());
    }

    [Fact]
    public void Write_still_shows_only_a_type_name_for_an_unknown_type_even_when_explicitly_marked_safe()
    {
        // Defense in depth carried over from QA-01: SafeLogValue vouches that a value is
        // not classified, not that its ToString() is safe to run. An unrecognised type is
        // still shown only as its type name, opt-in or not.
        const string secretValue = "synthetic-biometric-template-bytes";
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Payload", SafeLogValue.Of(new RecordWithCustomToString(secretValue))),
            new("{OriginalFormat}", "capture received: {Payload}"),
        };

        var json = WriteAndCapture(state, $"capture received: {secretValue}");
        var doc = JsonDocument.Parse(json);

        Assert.DoesNotContain(secretValue, json);
        Assert.Equal($"<{nameof(RecordWithCustomToString)}>", doc.RootElement.GetProperty("properties").GetProperty("Payload").GetString());
    }

    [Fact]
    public void Write_withholds_a_classified_scope_value_under_an_untrusted_key_name_by_default()
    {
        // The same default-withheld posture applies to BeginScope values: an unwrapped
        // value is withheld regardless of its key name or type.
        const string secretValue = "synthetic-signing-material";
        var state = new List<KeyValuePair<string, object?>> { new("{OriginalFormat}", "op complete") };
        var scope = new FakeScopeProvider(new Dictionary<string, object?>
        {
            ["diagnostic_context"] = secretValue,
        });

        var json = WriteAndCapture(state, "op complete", scope);
        var doc = JsonDocument.Parse(json);

        Assert.DoesNotContain(secretValue, json);
        Assert.Equal(
            BiologJsonConsoleFormatter.WithheldValue,
            doc.RootElement.GetProperty("scope").GetProperty("diagnostic_context").GetString());
    }

    [Fact]
    public void Write_withholds_an_exception_logged_as_an_unwrapped_property_value_by_default()
    {
        // Full-surface enumeration (BIO-FND-009-QA-05): an Exception passed as an ordinary
        // structured-log argument (not the log entry's own Exception) is untrusted like any
        // other unwrapped value - withheld by default, never its Message or ToString().
        const string secretValue = "synthetic-password-in-a-logged-exception-argument";
        var exception = new InvalidOperationException(secretValue);
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Error", exception),
            new("{OriginalFormat}", "failed: {Error}"),
        };

        var json = WriteAndCapture(state, $"failed: {exception}");
        var doc = JsonDocument.Parse(json);

        Assert.DoesNotContain(secretValue, json);
        Assert.Equal(BiologJsonConsoleFormatter.WithheldValue, doc.RootElement.GetProperty("properties").GetProperty("Error").GetString());
    }

    [Fact]
    public void Write_shows_only_a_type_name_for_an_exception_value_even_when_explicitly_marked_safe()
    {
        // Defense in depth: SafeLogValue vouches a value is not classified, not that its
        // ToString()/Message is safe to run - an Exception is never a safe scalar type, so
        // even an opted-in Exception still shows only its type name.
        const string secretValue = "synthetic-password-in-a-logged-exception-argument";
        var exception = new InvalidOperationException(secretValue);
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Error", SafeLogValue.Of(exception)),
            new("{OriginalFormat}", "failed: {Error}"),
        };

        var json = WriteAndCapture(state, $"failed: {exception}");
        var doc = JsonDocument.Parse(json);

        Assert.DoesNotContain(secretValue, json);
        Assert.Equal(
            $"<{nameof(InvalidOperationException)}>",
            doc.RootElement.GetProperty("properties").GetProperty("Error").GetString());
    }

    [Fact]
    public void Write_never_surfaces_the_exceptions_Data_dictionary()
    {
        // Full-surface enumeration (BIO-FND-009-QA-05): Exception.Data is an arbitrary,
        // caller-populated IDictionary some code uses to attach diagnostic context. This
        // formatter never reads it - confirmed here rather than merely assumed, since
        // Exception.ToString() (which this formatter deliberately never calls) also omits
        // it, and a future change adding exception detail must not reintroduce it silently.
        const string secretValue = "synthetic-secret-in-exception-data";
        var exception = new InvalidOperationException("ordinary message");
        exception.Data["classified"] = secretValue;

        var state = new List<KeyValuePair<string, object?>> { new("{OriginalFormat}", "boom") };
        var json = WriteAndCapture(state, "boom", exception: exception);

        Assert.DoesNotContain(secretValue, json);
    }

    private sealed record RecordWithCustomToString(string RawValue)
    {
        public override string ToString() => RawValue;
    }
}
