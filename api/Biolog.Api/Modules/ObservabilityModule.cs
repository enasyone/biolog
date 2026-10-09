using Biolog.Api.Observability;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Biolog.Api.Modules;

/// <summary>
/// Local observability baseline (BIO-FND-009): structured JSON console logs carrying
/// correlation/tenant context (AC01), OpenTelemetry tracing and metrics hooks with a
/// console exporter for local development (AC02), and the redaction default that keeps
/// classified values out of logs (AC04). See docs/OBSERVABILITY.md for the local developer
/// view (AC03) and the full enumerated list of paths a value can reach an observability
/// sink through (BIO-FND-009-QA-05).
///
/// Depends on <see cref="TenantModule"/> so <see cref="CorrelationLoggingMiddleware"/> runs
/// after <see cref="Biolog.Api.Tenancy.TenantContextMiddleware"/> has set (or, today,
/// deliberately left null) the tenant context it reads.
/// </summary>
public sealed class ObservabilityModule : IBiologModule
{
    public string Name => "Observability";
    public IReadOnlyCollection<string> DependsOn => new[] { "Tenant" };

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(logging =>
        {
            // BIO-FND-009-QA-05: WebApplication.CreateBuilder registers Debug and
            // EventSource providers by default alongside Console - every registered
            // ILoggerProvider receives every log event independently, and neither of those
            // two providers has ever heard of BiologJsonConsoleFormatter or Classify(). A
            // value withheld from the console JSON view was, until this fix, still fully
            // visible via an attached debugger's Output window or an ETW/EventPipe trace
            // capture. ClearProviders() removes all of them before Console is the only one
            // re-added - one sink, one redaction boundary, matching docs/OBSERVABILITY.md's
            // "no separate viewer to run" claim, which was not actually true before this.
            logging.ClearProviders();
            logging.AddConsole(options => options.FormatterName = BiologJsonConsoleFormatter.FormatterName);
            logging.AddConsoleFormatter<BiologJsonConsoleFormatter, ConsoleFormatterOptions>();
        });

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("Biolog.Api"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddConsoleExporter());
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationLoggingMiddleware>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
