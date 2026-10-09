# Local observability baseline (BIO-FND-009)

Local logs, traces and metrics for `api/Biolog.Api`, so a feature built by an agent can be
debugged on this machine before anything touches Azure. Implemented as one module,
`Modules/ObservabilityModule.cs`, following the pattern established by BIO-FND-008.

## Structured logs (AC01, AC04)

Every log line on stdout is one JSON object, written by `Observability/BiologJsonConsoleFormatter`
(registered as the `Microsoft.Extensions.Logging.Console` formatter named `biolog-json` -
it replaces only the *formatter* on the console provider the host already registers, not a
second competing provider). Shape:

```json
{
  "timestamp": "2026-09-21T19:59:38.9137910+00:00",
  "level": "Information",
  "category": "Microsoft.AspNetCore.Routing.EndpointMiddleware",
  "eventId": "ExecutingEndpoint",
  "message": "Executing endpoint '[WITHHELD]'",
  "properties": { "EndpointName": "[WITHHELD]" },
  "scope": {
    "SpanId": "[WITHHELD]",
    "TraceId": "[WITHHELD]",
    "correlation_id": "00-f7161f4bb0d181e1c2c4daf912f58c21-f24cad4f84690ebd-01",
    "tenant_id": null
  }
}
```

`EndpointName`, `SpanId`, and `TraceId` above are ASP.NET Core's own framework values, not
this module's - as of BIO-FND-009-QA-03 they render as `[WITHHELD]` because nothing
positively vouches for them (see "Default posture" below for why). `correlation_id` and
`tenant_id` still render because `CorrelationLoggingMiddleware` explicitly marks them safe.

`Observability/CorrelationLoggingMiddleware` pushes `correlation_id` and `tenant_id` onto
the log scope for the rest of the request. `correlation_id` is the current
`System.Diagnostics.Activity`'s id (so it lines up with the OpenTelemetry span below) or the
request's `TraceIdentifier` if tracing produced no activity; `tenant_id` is whatever
`ITenantContextAccessor.Context` holds - `null` in this shell, since BIO-FND-008's tenant
middleware deliberately resolves nothing yet (ADR-0002). That is the "where applicable" in
the acceptance criterion: a request without a resolved tenant logs `tenant_id: null` rather
than a fabricated value.

**Run it:** `dotnet run --project api/Biolog.Api/Biolog.Api.csproj` and watch stdout - no
extra setup, no docker container, no configuration flag.

### Redaction (AC04)

`Observability/SensitiveLogKeys` matches every structured property's and scope value's
**name** (not its value) against the "secret" and closely-related "sensitive" examples in
`contracts/schemas/data-classification.json` (ADR-0003 Decision 3 - the same registry Kemi's
audit/domain-event envelopes use): `password`, `token`, `credential`, `signing_key`,
`card_key`, `biometric*`, `private_key`, `connection_string`, `api_key`, `authorization`,
`secret`. A matched value is replaced with `[REDACTED]` - in the `properties`/`scope`
objects **and** in the rendered `message` string itself, since interpolating the original
value into `message` while redacting it in `properties` would leak it right back out a few
characters over (see `BiologJsonConsoleFormatter.RenderMessage`).

This is deliberately a denylist by property **name**, unlike ADR-0003's own audit-envelope
allowlist. An audit envelope is built once, centrally, from a known field set, so an
allowlist is cheap there; a log statement can appear at any call site with an arbitrary
message template, so a per-call-site allowlist isn't a "baseline" the rest of the platform
can build on without every future log line needing sign-off.

### Default posture: withheld unless the call site explicitly vouches for it (BIO-FND-009-QA-01 through QA-04)

Four independent review rounds moved this from "denylist plus a few extra checks" to a
real default-deny boundary, because each intermediate design had a hole the next one
closed:

- **Round 1 (QA-01):** a name-only denylist can't catch a classified *object* logged under
  an unrelated, generic name (`Payload`, `Detail`, ...) whose own `ToString()` returns a raw
  secret, or a secret embedded in an exception's `Message` (which has no name at all).
  First fix: a value-type allowlist - only render a value via its own `ToString()` when its
  *type* is one of a short list of scalars, and suppress exception messages entirely
  (`RenderExceptionSafely` keeps only the type name and stack trace).
- **Round 2 (QA-02):** that "safe type" list included `string` - and a classified value is,
  overwhelmingly, just a string. The exact same content the round-1 probe put in a custom
  object's `ToString()`, logged instead as a plain string under a generic name, sailed
  straight through. A type check can never tell a user id from a biometric template; they
  are the same CLR type. Fix: trust the log entry's **category** when it starts with a known
  .NET framework namespace, and a small fixed set of **scope key names** otherwise, and
  require an explicit `SafeLogValue.Of(value)` opt-in for everything else.
- **Round 3 (QA-03):** both round-2 trust sources were themselves forgeable. A logger
  category is just a string any caller supplies to `ILoggerFactory.CreateLogger(string)` -
  `CreateLogger("Microsoft.Whatever")` is one call, from anywhere, and gets treated as
  framework-trusted. A `BeginScope` dictionary key is exactly as forgeable: any caller can
  push a dictionary containing `"tenant_id"`. Neither is authenticated identity; both are
  labels the value's own producer chooses. Fix: remove both mechanisms entirely; only an
  explicit `SafeLogValue.Of(value)` opt-in establishes trust, applied uniformly including to
  this module's own `CorrelationLoggingMiddleware`.
- **Round 4 (QA-04):** a completely separate code path bypassed all of the above. A log
  entry whose state is not the standard structured property list (a raw `ILogger.Log<TState>`
  call with a custom `TState`, rather than the templated `LogInformation`/`LogError`/...
  extensions) fell back to calling `logEntry.Formatter(state, exception)` directly - an
  arbitrary delegate the CALLER supplied, whose return string never passed through
  `Classify` at all. Fix: that delegate is never invoked any more. The raw state goes
  through the identical `Classify` boundary as any other value, which means the message
  renders only when the state itself is a `SafeLogValue`.

**The only thing that establishes trust now is `Observability.SafeLogValue.Of(value)`** - an
explicit, visible-in-source-review assertion, at the one call site that computed a specific
value, that it is not classified. Nothing else grants it: not a name, not a type, not a
category, not a scope key. `CorrelationLoggingMiddleware` wraps `correlation_id` and
`tenant_id` in `SafeLogValue.Of(...)` itself, exactly like any other call site would -
there is no special-cased trust for this module's own middleware either.

A value with no `SafeLogValue` wrapper renders as `[WITHHELD]` - deliberately a different
marker from `[REDACTED]` (a `SensitiveLogKeys` name match, which still fires even on a
wrapped value: the opt-in covers a value with no name signal, it does not override one that
exists). An unrecognised *type*, wrapped or not, is still shown only as its type name (e.g.
`<BiometricPayload>`), never `[WITHHELD]` and never its own `ToString()` - the opt-in
vouches that a value is not classified, not that running its `ToString()` is safe.

**Accepted costs**, named because they are real and got strictly larger each round:

- Every value any future BIOLOG feature logs is invisible by default until that call site
  explicitly opts it in with `SafeLogValue.Of(...)`. There is no way around this that does
  not reintroduce a name, type, or label heuristic - all three have already been shown to
  leak or be forgeable.
- **ASP.NET Core's own framework request-diagnostics logging is now withheld too** -
  routing, hosting, and health-check property values (`EndpointName`, `StatusCode`, request
  timings) render as `[WITHHELD]`, since Microsoft's own source cannot be changed to add a
  `SafeLogValue` wrapper. This is new as of round 3 and a genuine loss of local-dev
  convenience: earlier versions of this document showed a `properties`/`scope`-rich example
  request log line that no longer renders that way. No acceptance criterion requires that
  richness (AC01 only requires this module's own `correlation_id`/`tenant_id`, which still
  render); AC04's "excluded by default" is the one criterion that cannot be traded for it.
- A genuinely harmless exception message is suppressed exactly as readily as a harmful one,
  for the same reason as round 1.

### One residual risk with no runtime fix: string interpolation instead of a template (BIO-FND-009-QA-04)

Everything above assumes a log call uses the structured `{Placeholder}` template syntax
(`logger.LogInformation("User {UserId}", userId)`). If a call site instead uses C# string
interpolation (`logger.LogInformation($"User {userId}")`), the interpolation happens in the
**caller's compiled code**, before any `ILogger` method runs. By the time this formatter
sees the log entry, the classified value is already baked into a single literal string with
no placeholder, no separate argument, and nothing for `Classify` to intercept - the exact
same structural limit as QA-02's "a string has no inherent classification," one level up:
now it is "a template string can't tell a compile-time literal from an already-interpolated
runtime value."

There is no runtime control possible here. The mitigation is a compile-time one:
`api/Biolog.Api/.editorconfig` promotes .NET's own **CA2254** analyzer ("the logging
message template should not vary between calls," which also fires on an interpolated
template) from its default severity to a **build error**, so this pattern fails the build
before it ever reaches production rather than silently defeating every default above.

### Full enumeration of paths to an observability sink (BIO-FND-009-QA-05)

Four review rounds (QA-01 through QA-04) each found a genuinely different bypass of the
same boundary, one probe at a time: name blocklist, then type-shape allowlist, then
forgeable trust labels, then a completely separate code path (custom `TState`). Rather than
wait for a fifth probe to find the next one, every path through which a value could reach
an observability sink in this pipeline was enumerated and checked in one pass:

| Path | Status |
|---|---|
| Message template placeholder args (`{Placeholder}`) | Covered - `Classify` via `RenderMessage` |
| Message template literal text via C# string interpolation (`$"..."`) | **No runtime control possible** - closed at compile time (CA2254 error, above) |
| Custom `TState` + caller-supplied `Func<TState, Exception?, string>` formatter | Covered - `RenderNonStructuredMessage`; the delegate is never invoked (QA-04) |
| `BeginScope` dictionary/KVP-shaped values | Covered - `Classify` via `WriteScope` |
| `BeginScope` non-KVP arbitrary scope object | Covered - routed through `Classify` under the literal key `"scope"`, not stringified directly |
| `LogEntry.Exception.Message` / `.ToString()` | Covered - `RenderExceptionSafely` keeps only type name + stack trace, across the whole `InnerException` chain |
| An `Exception` passed as an ordinary structured-log **argument** (not the entry's own exception) | Covered - not a safe scalar type, so it is withheld by default like any other unwrapped value, and shows only its type name even if explicitly marked safe (never its own `ToString()`/`Message`) |
| `Exception.Data` (an arbitrary caller-populated dictionary) | Covered by omission - never read anywhere in this formatter; confirmed with a regression rather than assumed |
| A nested structured value (a KVP list, or other non-scalar shape, as a property/scope value) | Covered - not a safe scalar or scalar sequence, so it renders as only its type name/withheld rather than being unpacked |
| **Other registered `ILoggerProvider`s** (`Debug`, `EventSource`, `EventLog` on Windows) | **Was NOT covered - fixed this round (QA-05).** `WebApplication.CreateBuilder` registers all of these by default; each receives every log event independently and has never heard of `BiologJsonConsoleFormatter`. `ObservabilityModule` now calls `ClearProviders()` before registering only the redacting console provider - one sink, one boundary. |
| OpenTelemetry span/metric attributes (`Activity.SetTag`, custom instrumentation) | Not wired today - no code in this codebase calls `Activity.Current?.SetTag(...)` or otherwise attaches custom data to a span/metric; `.WithLogging()` was never wired, so OpenTelemetry does not capture `ILogger` records at all. Not just a doc/grep claim: `ObservabilityTests.AC04_a_secret_in_a_request_header_does_not_appear_anywhere_in_captured_output` sends a real request with a synthetic secret in a header and asserts it appears nowhere in stdout - including whatever the console span exporter prints for that request - so this becomes a failing regression the moment a later story adds header/tag enrichment without updating this table. |
| ASP.NET Core's `Microsoft.AspNetCore.HttpLogging` middleware (full request/response body logging) | Not wired today - confirmed `UseHttpLogging()`/`AddHttpLogging()` are not called anywhere in this codebase; the package only appears as a transitive NuGet reference, never actually wired in. Covered by the same header-secret regression above: if `UseHttpLogging()` were added, that header would appear in its output during the exact request the test sends. |

Both rows above are watched, not merely trusted: unlike an earlier version of this table,
each now has an executable regression a future change would have to break to reintroduce
the risk, not only documentation someone has to remember to re-read.

## OpenTelemetry hooks (AC02)

`ObservabilityModule` registers OpenTelemetry tracing and metrics
(`OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Instrumentation.AspNetCore`), each with
a console exporter for local development. Every inbound HTTP request produces a span (method,
route, status code, duration) and ASP.NET Core's request-duration/count metrics are
exported, both printed to stdout alongside the JSON logs.

**Run it:** the same `dotnet run` - trace/metric blocks appear on stdout interleaved with the
JSON log lines (a plain-text block per export, not JSON; a script parsing only the log
stream should filter to lines starting with `{`, as `tests/dotnet/Biolog.IntegrationTests/ObservabilityTests.cs`
does).

**This is a hook, not a collector.** No Jaeger/Prometheus/Seq container exists yet in
`infrastructure/docker-compose.local.yml` - wiring a real local collector, and swapping the
console exporter for an OTLP one pointed at it, is later infrastructure work this module's
shape does not need to change for. "Hooks exist" is this story's literal acceptance
criterion; a full local observability stack is not.

## Local developer view (AC03)

There is no separate viewer to run. `dotnet run` and read stdout:

- Structured JSON logs, one object per line - `jq` or any JSON-line viewer works, e.g.
  `dotnet run --project api/Biolog.Api/Biolog.Api.csproj 2>&1 | grep -E '^\{' | jq .`
- OpenTelemetry trace/metric export blocks, printed as plain text between log lines.

`correlation_id` in a log line's `scope` matches the `TraceId`/`SpanId` printed in that
request's trace export block, so a developer can jump between "what happened" (logs) and
"how long each step took" (trace) for the same request without a separate correlation
scheme.

## Developer tests

- `tests/dotnet/Biolog.UnitTests/Observability/SensitiveLogKeysTests.cs` - name matching for
  every classified example plus non-classified names (AC04).
- `tests/dotnet/Biolog.UnitTests/Observability/BiologJsonConsoleFormatterTests.cs` - scope
  rendering (AC01), redaction in both `properties` and the rendered `message` (AC04),
  exception rendering, and the non-templated-state fallback.
- `tests/dotnet/Biolog.IntegrationTests/ObservabilityTests.cs` - through the real composed
  host: `TracerProvider`/`MeterProvider` resolve from DI (AC02), a real HTTP request's
  console output carries `correlation_id`/`tenant_id` (AC01), and a log call through the
  actual registered provider (not a hand-built formatter instance) redacts a secret-named
  value end to end (AC04).
- `tests/foundation/BIO-FND-009.test.sh` - offline-safe structural wiring checks.
