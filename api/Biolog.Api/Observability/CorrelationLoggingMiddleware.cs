using Biolog.Api.Tenancy;

namespace Biolog.Api.Observability;

/// <summary>
/// Pushes <c>correlation_id</c> and <c>tenant_id</c> onto every log scope for the
/// remainder of the request (BIO-FND-009-AC01). Runs after
/// <see cref="TenantContextMiddleware"/> (see <c>Modules/ObservabilityModule.cs</c>'s
/// <c>DependsOn</c>) so it reads whatever that hook left in
/// <see cref="ITenantContextAccessor"/> - <c>null</c> in this shell, a real value once a
/// future story implements ADR-0002, with no change needed here either time. That is the
/// "where applicable" in the acceptance criterion: a request with no resolved tenant logs
/// <c>tenant_id: null</c> rather than a fabricated placeholder, matching
/// <see cref="TenantContextMiddleware"/>'s own contract of never inventing one.
///
/// The correlation id is <see cref="System.Diagnostics.Activity.Current"/>'s id when
/// OpenTelemetry's ASP.NET Core instrumentation has started one for this request, falling
/// back to <see cref="HttpContext.TraceIdentifier"/> otherwise - so a request is always
/// correlatable even if tracing is disabled, and correlates with the OpenTelemetry span
/// (BIO-FND-009-AC02) when it is not.
///
/// Both values are wrapped in <see cref="SafeLogValue"/> explicitly (BIO-FND-009-QA-03):
/// <see cref="BiologJsonConsoleFormatter"/> no longer trusts a scope value merely because
/// its key is named <c>correlation_id</c>/<c>tenant_id</c> - a logger category string and a
/// scope dictionary key are both caller-controlled labels, not authenticated provenance, so
/// any application code could otherwise push its own <c>tenant_id</c> key carrying a
/// classified value and have it rendered. The explicit wrapper here is this middleware -
/// the one place that actually computes these two values - vouching for them at the one
/// point that can.
/// </summary>
public sealed class CorrelationLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationLoggingMiddleware> _logger;

    public CorrelationLoggingMiddleware(RequestDelegate next, ILogger<CorrelationLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContextAccessor accessor)
    {
        var correlationId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;
        var scope = new Dictionary<string, object?>
        {
            ["correlation_id"] = SafeLogValue.Of(correlationId),
            ["tenant_id"] = SafeLogValue.Of(accessor.Context?.TenantId),
        };

        // Must await inside the scope, not return the Task and let `using` dispose on
        // synchronous exit - disposing here ends the scope only once the whole downstream
        // pipeline has actually completed, not merely started.
        using (_logger.BeginScope(scope))
        {
            await _next(context);
        }
    }
}
