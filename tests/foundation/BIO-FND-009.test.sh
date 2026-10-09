#!/usr/bin/env bash
# Offline-safe structural checks for BIO-FND-009 (local observability baseline).
# Behavioural assertions (redaction, scope rendering, real end-to-end wiring through the
# composed host) live in the executable xunit suites under tests/dotnet/ (see
# docs/OBSERVABILITY.md, docs/TESTING.md, scripts/quality.sh --full).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

# --- AC01: structured logs contain correlation and tenant context where applicable ---
check "AC01 correlation logging middleware exists" test -f api/Biolog.Api/Observability/CorrelationLoggingMiddleware.cs
check "AC01 middleware pushes correlation_id and tenant_id onto the log scope" \
  bash -c 'grep -q "correlation_id" api/Biolog.Api/Observability/CorrelationLoggingMiddleware.cs && grep -q "tenant_id" api/Biolog.Api/Observability/CorrelationLoggingMiddleware.cs'
check "AC01 middleware reads the tenant hook rather than inventing a value" \
  bash -c 'grep -q "ITenantContextAccessor" api/Biolog.Api/Observability/CorrelationLoggingMiddleware.cs'
check "AC01 the JSON console formatter renders scope values" \
  bash -c 'grep -q "WriteScope" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "AC01 ObservabilityModule wires the middleware into the pipeline" \
  bash -c 'grep -q "UseMiddleware<CorrelationLoggingMiddleware>" api/Biolog.Api/Modules/ObservabilityModule.cs'
check "AC01 ObservabilityModule runs after tenant context is set" \
  bash -c 'grep -q "\"Tenant\"" api/Biolog.Api/Modules/ObservabilityModule.cs'

# --- AC02: OpenTelemetry hooks exist ---
check "AC02 OpenTelemetry packages referenced" \
  bash -c 'grep -q "OpenTelemetry.Extensions.Hosting" api/Biolog.Api/Biolog.Api.csproj && grep -q "OpenTelemetry.Instrumentation.AspNetCore" api/Biolog.Api/Biolog.Api.csproj'
check "AC02 tracing and metrics are both registered" \
  bash -c 'grep -q "WithTracing" api/Biolog.Api/Modules/ObservabilityModule.cs && grep -q "WithMetrics" api/Biolog.Api/Modules/ObservabilityModule.cs'
check "AC02 ASP.NET Core instrumentation is wired to both signals" \
  bash -c '[ "$(grep -c "AddAspNetCoreInstrumentation" api/Biolog.Api/Modules/ObservabilityModule.cs)" -ge 2 ]'
check "AC02 a local-development exporter is configured (no external collector required)" \
  bash -c 'grep -q "AddConsoleExporter" api/Biolog.Api/Modules/ObservabilityModule.cs'

# --- AC03: local developer view is documented ---
check "AC03 docs/OBSERVABILITY.md exists" test -f docs/OBSERVABILITY.md
check "AC03 doc explains how to run and read logs locally" \
  bash -c 'grep -q "dotnet run" docs/OBSERVABILITY.md'
check "AC03 doc explains the trace/metric local view" \
  bash -c 'grep -qi "OpenTelemetry" docs/OBSERVABILITY.md'

# --- AC04: secrets and biometric payloads are excluded from logs by default ---
check "AC04 sensitive key matcher exists" test -f api/Biolog.Api/Observability/SensitiveLogKeys.cs
check "AC04 matcher covers secret-classified examples from the shared registry" \
  bash -c 'grep -q "password" api/Biolog.Api/Observability/SensitiveLogKeys.cs && grep -qi "biometric" api/Biolog.Api/Observability/SensitiveLogKeys.cs && grep -q "token" api/Biolog.Api/Observability/SensitiveLogKeys.cs'
check "AC04 matcher references the shared data-classification registry (ADR-0003)" \
  bash -c 'grep -q "data-classification.json" api/Biolog.Api/Observability/SensitiveLogKeys.cs'
check "AC04 the formatter redacts structured properties" \
  bash -c 'grep -q "IsSensitive" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "AC04 the rendered message is redacted too, not only the properties object" \
  bash -c 'grep -q "RenderMessage" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "AC04 redaction is wired as the default console formatter, not opt-in" \
  bash -c 'grep -q "AddConsoleFormatter<BiologJsonConsoleFormatter" api/Biolog.Api/Modules/ObservabilityModule.cs'

# --- QA-01: default redaction must not depend on the caller choosing a recognised name ---
check "QA-01 a type-shape allowlist guards values regardless of property name" \
  bash -c 'grep -q "SafeScalarTypes" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs && grep -q "IsSafeScalar" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "QA-01 an unrecognised value type is never rendered via its own ToString()" \
  bash -c 'grep -q "value.GetType().Name" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "QA-01 scope values flow through the same type-shape guard, not a bare ToString()" \
  bash -c '! grep -q "scope.ToString()" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "QA-01 exception messages are suppressed by default; type and stack trace are kept" \
  bash -c 'grep -q "RenderExceptionSafely" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs && ! grep -q "logEntry.Exception.ToString()" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'

# --- QA-02: a string has no inherent classification, so type alone cannot mean "safe" ---
check "QA-02 an explicit opt-in type exists for call sites to assert a value is safe" \
  test -f api/Biolog.Api/Observability/SafeLogValue.cs
check "QA-02 an unknown value defaults to withheld, distinct from a name-based redaction" \
  bash -c 'grep -q "WithheldValue" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "QA-02 an explicit safe-value opt-in still yields to name-based redaction" \
  bash -c 'grep -q "SafeLogValue" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs && grep -q "IsSensitive(key)" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'

# --- QA-03: a logger category string and a scope key name are caller-controlled labels, not identity ---
check "QA-03 category-prefix trust was removed, not merely narrowed" \
  bash -c '! grep -q "TrustedCategoryPrefixes" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs && ! grep -q "IsTrustedCategory" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "QA-03 fixed scope-key-name trust was removed, not merely narrowed" \
  bash -c '! grep -q "TrustedScopeKeys" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "QA-03 SafeLogValue is the only input Classify uses to establish trust" \
  bash -c 'grep -q "private static Classification Classify(string key, object. value)" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "QA-03 the middleware's own correlation/tenant values go through the same opt-in as any caller" \
  bash -c 'grep -q "SafeLogValue.Of(correlationId)" api/Biolog.Api/Observability/CorrelationLoggingMiddleware.cs && grep -q "SafeLogValue.Of(accessor.Context" api/Biolog.Api/Observability/CorrelationLoggingMiddleware.cs'

# --- QA-04: a custom TState's formatter is arbitrary caller code, not a value Classify ever sees ---
check "QA-04 the caller's formatter delegate is never invoked for non-structured state" \
  bash -c '! grep -q "logEntry.Formatter(" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "QA-04 non-structured state is withheld by default via the same Classify boundary" \
  bash -c 'grep -q "RenderNonStructuredMessage" api/Biolog.Api/Observability/BiologJsonConsoleFormatter.cs'
check "QA-04 the string-interpolation residual risk is closed at compile time, not left silent" \
  test -f api/Biolog.Api/.editorconfig
check "QA-04 CA2254 is promoted to a build error" \
  bash -c 'grep -q "dotnet_diagnostic.CA2254.severity = error" api/Biolog.Api/.editorconfig'

# --- QA-05: every default logger provider bypasses Classify except the one this module owns ---
check "QA-05 default logger providers are cleared before the redacting one is added" \
  bash -c 'grep -q "ClearProviders" api/Biolog.Api/Modules/ObservabilityModule.cs'
check "QA-05 docs enumerate every path to an observability sink" \
  bash -c 'grep -q "Full enumeration of paths to an observability sink" docs/OBSERVABILITY.md'
check "QA-05 the two documented-only rows also have an executable regression" \
  test -f tests/dotnet/Biolog.IntegrationTests/ObservabilityTests.cs
check "QA-05 a real request carrying a secret is proven, not merely assumed, not to leak" \
  bash -c 'grep -q "AC04_a_secret_in_a_request_header_does_not_appear_anywhere_in_captured_output" tests/dotnet/Biolog.IntegrationTests/ObservabilityTests.cs'

# --- Module registration ---
check "Program.cs registers ObservabilityModule through the module host" \
  bash -c 'grep -q "new ObservabilityModule()" api/Biolog.Api/Program.cs'

# --- Developer test wiring ---
check "unit tests cover sensitive key matching" \
  test -f tests/dotnet/Biolog.UnitTests/Observability/SensitiveLogKeysTests.cs
check "unit tests cover the JSON console formatter" \
  test -f tests/dotnet/Biolog.UnitTests/Observability/BiologJsonConsoleFormatterTests.cs
check "integration tests exercise the real composed host" \
  test -f tests/dotnet/Biolog.IntegrationTests/ObservabilityTests.cs
check "builder evidence recorded" test -d evidence/BIO-FND-009/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
