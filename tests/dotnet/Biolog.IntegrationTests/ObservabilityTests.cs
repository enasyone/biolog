using System.Net;
using System.Text.Json;
using Biolog.Api.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Biolog.IntegrationTests;

/// <summary>Developer tests for BIO-FND-009: local observability baseline, exercised
/// through the real composed host (module wiring, DI registration, actual console
/// provider) rather than unit-level assertions on the formatter class alone.</summary>
public sealed class ObservabilityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ObservabilityTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public void AC02_opentelemetry_tracing_and_metrics_providers_are_registered()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<TracerProvider>());
        Assert.NotNull(scope.ServiceProvider.GetService<MeterProvider>());
    }

    [Fact]
    public void AC04_only_the_redacting_console_provider_is_registered()
    {
        // Regression for BIO-FND-009-QA-05: WebApplication.CreateBuilder registers Debug
        // and EventSource providers by default. Both receive every log event independently
        // of BiologJsonConsoleFormatter and have never heard of Classify() - a value
        // withheld from the console JSON view was still fully visible through either one.
        // Exactly one provider, of the expected redacting type, must be registered.
        using var scope = _factory.Services.CreateScope();
        var providers = scope.ServiceProvider.GetServices<ILoggerProvider>().ToList();

        var provider = Assert.Single(providers);
        Assert.Equal("Microsoft.Extensions.Logging.Console.ConsoleLoggerProvider", provider.GetType().FullName);
    }

    [Fact]
    public async Task AC01_a_real_request_logs_correlation_and_tenant_scope_to_the_console_view()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            using var client = _factory.CreateClient();
            var response = await client.GetAsync("/health/ready", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // The console logger queues writes on a background thread; give it a moment.
            await Task.Delay(200, cancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Only our own formatter's lines are JSON; other providers (e.g. Debug) or the test
        // host itself may interleave plain-text lines on the same redirected Console.Out.
        var lines = captured.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.TrimStart().StartsWith('{'))
            .Select(line =>
            {
                try
                {
                    return JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    return null;
                }
            })
            .Where(doc => doc is not null)
            .Select(doc => doc!)
            .ToList();

        var withCorrelation = lines.FirstOrDefault(doc =>
            doc.RootElement.TryGetProperty("scope", out var scope) &&
            scope.TryGetProperty("correlation_id", out _));

        Assert.NotNull(withCorrelation);
        var scopeElement = withCorrelation!.RootElement.GetProperty("scope");
        Assert.False(string.IsNullOrEmpty(scopeElement.GetProperty("correlation_id").GetString()));
        Assert.True(scopeElement.TryGetProperty("tenant_id", out var tenantId));
        Assert.Equal(JsonValueKind.Null, tenantId.ValueKind); // no resolver yet (BIO-FND-008-AC04)
    }

    [Fact]
    public async Task AC04_a_secret_named_value_is_redacted_by_the_real_wired_console_provider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            using var scope = _factory.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Biolog.IntegrationTests.RedactionProbe");

            logger.LogInformation(
                "Synthetic credential check for {UserId} using {Password}",
                SafeLogValue.Of("u_synthetic"),
                "hunter2-synthetic");

            await Task.Delay(200, cancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = captured.ToString();
        Assert.Contains(SensitiveLogKeys.RedactedValue, output);
        Assert.DoesNotContain("hunter2-synthetic", output);
        Assert.Contains("u_synthetic", output); // explicitly marked safe by the call site
    }

    [Fact]
    public async Task AC04_a_classified_object_under_a_generic_name_is_withheld_by_the_real_wired_console_provider()
    {
        // Regression for BIO-FND-009-QA-01, through the real DI-wired provider rather than a
        // hand-built formatter instance: a value logged under a name SensitiveLogKeys has no
        // reason to flag (e.g. "Payload") must still be excluded by default.
        const string classifiedValue = "synthetic-biometric-template-bytes";
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            using var scope = _factory.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Biolog.IntegrationTests.RedactionProbe");

            logger.LogInformation("Capture received: {Payload}", new ClassifiedPayload(classifiedValue));

            await Task.Delay(200, cancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.DoesNotContain(classifiedValue, captured.ToString());
    }

    [Fact]
    public async Task AC04_a_plain_string_under_a_generic_name_is_withheld_by_default_through_the_real_wired_console_provider()
    {
        // Regression for BIO-FND-009-QA-02: the same classified content, this time as a
        // plain string (not a caller-defined object) under an unrecognised name, through
        // the real DI-wired provider rather than a hand-built formatter instance.
        const string classifiedValue = "synthetic-biometric-template-as-generic-string";
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            using var scope = _factory.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Biolog.IntegrationTests.RedactionProbe");

            logger.LogInformation("Capture text: {PayloadText}", classifiedValue);

            await Task.Delay(200, cancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.DoesNotContain(classifiedValue, captured.ToString());
    }

    [Fact]
    public async Task AC04_a_forged_framework_category_does_not_grant_trust_through_the_real_wired_console_provider()
    {
        // Regression for BIO-FND-009-QA-03: any application call site can request
        // ILoggerFactory.CreateLogger("Microsoft.Whatever") through the real, unmodified
        // ILoggerFactory - a category string is a caller-chosen label, not proof the call
        // originates from .NET's own framework code.
        const string classifiedValue = "synthetic-biometric-template-as-generic-string";
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            using var scope = _factory.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Microsoft.SyntheticApplicationProbe");

            logger.LogInformation("Capture text: {PayloadText}", classifiedValue);

            await Task.Delay(200, cancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.DoesNotContain(classifiedValue, captured.ToString());
    }

    [Fact]
    public async Task AC04_a_custom_state_formatters_output_is_withheld_through_the_real_wired_console_provider()
    {
        // Regression for BIO-FND-009-QA-04: a raw ILogger.Log<TState> call with a
        // non-structured custom state and an arbitrary formatter delegate, through the real
        // DI-wired provider - the delegate's return value must never reach the log.
        const string classifiedValue = "synthetic-biometric-from-custom-formatter";
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            using var scope = _factory.Services.CreateScope();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Biolog.IntegrationTests.RedactionProbe");

            logger.Log(LogLevel.Information, new EventId(1, "CustomState"), "opaque-state", null,
                (_, _) => classifiedValue);

            await Task.Delay(200, cancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.DoesNotContain(classifiedValue, captured.ToString());
    }

    [Fact]
    public async Task AC04_a_secret_in_a_request_header_does_not_appear_anywhere_in_captured_output()
    {
        // Kemi's design review (BIO-FND-009-QA-05 follow-up): the enumeration table's two
        // "not applicable today" rows (OpenTelemetry span/metric attributes via a future
        // Enrich/SetTag callback, and ASP.NET Core's HttpLogging middleware) were proven by
        // absence-of-code (grep) rather than an executable probe, unlike every other row.
        // This sends a real request carrying a synthetic secret in a header and captures
        // everything this pipeline writes to stdout during it - the JSON console logs AND
        // whatever the OpenTelemetry console exporter prints for that request's span - in
        // one window. It passes today because neither mechanism is wired; it becomes a
        // permanent regression the moment a later story adds header enrichment, custom span
        // tagging, or HttpLogging without revisiting this table first.
        const string secretHeaderValue = "synthetic-secret-in-a-request-header";
        var cancellationToken = TestContext.Current.CancellationToken;
        var originalOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            using var client = _factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health/ready");
            request.Headers.Add("X-Synthetic-Secret", secretHeaderValue);
            var response = await client.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await Task.Delay(200, cancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.DoesNotContain(secretHeaderValue, captured.ToString());
    }

    private sealed record ClassifiedPayload(string RawValue)
    {
        public override string ToString() => RawValue;
    }
}
