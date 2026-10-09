# BIO-FND-009 — Builder evidence

**Story:** Implement local observability baseline
**Owner:** Emeka
**Worktree:** `biolog-worktrees/BIO-FND-009`, branch `feature/BIO-FND-009` off `develop`
**Dependency:** BIO-ARC-003 (DONE — audit/event envelopes and the shared data-classification registry)
**Commit:** `7662a21fe59d5fa8882bf197dcfdb43d070a089f`

## AC to evidence map

| AC | What was built | Evidence |
|----|-----------------|----------|
| AC01 — structured logs contain correlation and tenant context where applicable | `Observability/CorrelationLoggingMiddleware` pushes `correlation_id` (current `Activity.Id`, falling back to `TraceIdentifier`) and `tenant_id` (from `ITenantContextAccessor`, `null` today per BIO-FND-008) onto every log scope for the request. `Observability/BiologJsonConsoleFormatter` renders scope values into every JSON log line. | `dotnet-unit-test-output.txt` (`BiologJsonConsoleFormatterTests` scope cases), `dotnet-integration-test-output.txt` (`ObservabilityTests.AC01_...`), `structural-test-output.txt` |
| AC02 — OpenTelemetry hooks exist | `ObservabilityModule` registers `OpenTelemetry.Extensions.Hosting` tracing and metrics, `OpenTelemetry.Instrumentation.AspNetCore`, and a console exporter for local development — live-verified with `dotnet run` (a real request produced a printed `Activity`/span with matching trace/span ids). | `dotnet-integration-test-output.txt` (`ObservabilityTests.AC02_...`), `structural-test-output.txt` |
| AC03 — local developer view is documented | `docs/OBSERVABILITY.md`: how to run, how to read the JSON log lines and OTel console export blocks, and how `correlation_id` in a log line ties back to a request's trace. | `structural-test-output.txt` |
| AC04 — secrets and biometric payloads are excluded from logs by default | `Observability/SensitiveLogKeys` matches structured property/scope names against `contracts/schemas/data-classification.json`'s "secret" examples (ADR-0003). `BiologJsonConsoleFormatter` redacts matched values in both the `properties`/`scope` objects **and** the re-rendered `message` string (a real bug caught and fixed before committing — see below). | `dotnet-unit-test-output.txt` (`SensitiveLogKeysTests`, `BiologJsonConsoleFormatterTests` redaction cases), `dotnet-integration-test-output.txt` (`ObservabilityTests.AC04_...`, redaction through the real DI-wired provider, not a hand-built formatter instance), `structural-test-output.txt` |

## Developer tests (70 pass)

- `tests/dotnet/Biolog.UnitTests/Observability/SensitiveLogKeysTests.cs` (23 tests) — AC04 name matching.
- `tests/dotnet/Biolog.UnitTests/Observability/BiologJsonConsoleFormatterTests.cs` (19 tests) — AC01 scope rendering, AC04/QA-01 through QA-05 default-withheld posture across every enumerated path (message-string regression, generic-name object, classified scope value, plain-string and string-array withheld-by-default, `SafeLogValue` opt-in, forged-category-name regression, unwrapped-trusted-key regression, unknown-type-even-when-explicitly-marked-safe, custom-state withheld by default, formatter delegate never invoked, `SafeLogValue`-wrapped custom state renders, exception-as-argument withheld/type-name-only, `Exception.Data` never surfaced), exception message suppression.
- `tests/dotnet/Biolog.IntegrationTests/ObservabilityTests.cs` (9 tests) — through the real composed host: `TracerProvider`/`MeterProvider` resolve from DI (AC02), a real HTTP request's console output carries correlation/tenant scope (AC01), a log call through the actual registered provider withholds a generic-named classified object, a plain classified string, a value logged under a literally forged `"Microsoft."`-prefixed category, and a raw `ILogger.Log<TState>` call's custom formatter output, `SafeLogValue.Of` renders an explicitly-marked-safe value end to end (AC04/QA-01 through QA-04), exactly one `ILoggerProvider` (the redacting console one) is registered (QA-05), and a secret in a real request header appears nowhere in captured output (Kemi's QA-05 follow-up recommendation).
- `tests/foundation/BIO-FND-009.test.sh` (43 checks) — offline-safe structural wiring, wired into `scripts/quality.sh` in the same commit it was added (not left for a later fix, unlike BIO-FND-008's harness).

Reproduce with:
```sh
dotnet test tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
bash scripts/check-dotnet-vulnerabilities.sh api/Biolog.Api/Biolog.Api.csproj tests/dotnet/Biolog.UnitTests/Biolog.UnitTests.csproj tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
bash tests/foundation/BIO-FND-009.test.sh
bash evidence/BIO-FND-009/codex/independent-review.sh
```
(the exact `test_command` in `metadata.json`, chained with `&&`, exits 0.)

## CHANGES_REQUIRED round 1 (QA-01): name-only redaction leaked classified values under generic names and in exceptions

Seyi's independent review (`evidence/BIO-FND-009/codex/REVIEW.md`, probe at
`evidence/BIO-FND-009/codex/RedactionProbe/`) found two leaks a property-NAME denylist can
never close: a classified object logged under an unrelated, generic name (e.g. `Payload`)
whose own `ToString()` returns the raw value, and a secret embedded directly in an
exception's `Message` (which has no name to match at all).

Fixed (`3f7ed20`) with two defaults independent of naming, both applied to `message`,
`properties`, and `scope` alike:

1. **Type-shape allowlist.** `BiologJsonConsoleFormatter` now only calls `ToString()` on a
   value whose type is one of a short allowlist of definitely-safe scalars (`string`,
   numeric types, `bool`, `DateTime`/`DateTimeOffset`, `Guid`, `Uri`, enums) or a non-string
   `IEnumerable` of such scalars — mirroring ADR-0003 Decision 3's "an array of anything but
   primitives is withheld outright." Any other type renders as only its type name (e.g.
   `<BiometricPayload>`), regardless of the key it is logged under. This also fixed a
   related bypass in `WriteScope`: a `BeginScope` value that wasn't already a key/value
   dictionary was previously stringified directly (`scope.ToString()`) with no guard at all.
2. **Exception messages suppressed by default.** `RenderExceptionSafely` replaces the
   previous `Exception.ToString()` call: keeps the type name and full stack trace across the
   whole `InnerException` chain, drops the message text entirely.

Both are named, accepted trade-offs: a legitimate value under an unrecognised type now
shows only its type name, and a harmless exception message is suppressed exactly as readily
as a harmful one. Re-ran Seyi's own `independent-review.sh` probe unmodified after the fix:
`PASS classified payloads are excluded` (previously reproduced both leaks).

**Superseded by round 2 below**: the type-shape allowlist's inclusion of `string` as a
"definitely-safe scalar" is exactly what round 2 found wrong.

## CHANGES_REQUIRED round 2 (QA-02): a string has no inherent classification, so treating every string as safe reopened the same hole

Seyi's re-review (`evidence/BIO-FND-009/codex/REREVIEW-ROUND-2.md`) found that the round-1
type allowlist's inclusion of `string` (and arrays of strings) let the *exact same*
classified content through again, this time as a plain string under a generic key
(`PayloadText`) instead of a custom object. A biometric template, a token, and a user id
are all just strings; no type check can tell them apart.

Fixed (`c9f636d`) by flipping the default: a value is now rendered only when something
*positively* trusts it — never the value's own name or type. Three trust sources: the log
entry's category (`Microsoft.`/`System.`/`OpenTelemetry.` — framework diagnostics, never
caller business data), a small fixed scope-key allowlist (`correlation_id`/`tenant_id` plus
ASP.NET Core's own request-diagnostics keys), and an explicit
`Observability/SafeLogValue.Of(value)` opt-in a call site uses only after verifying a
specific value is safe — still yielding to `SensitiveLogKeys` name-matching. Everything else
renders as `[WITHHELD]`, distinct from `[REDACTED]` (a specific classification claim this
marker does not make). See `docs/OBSERVABILITY.md` "Default posture: withheld unless
something positively trusts it" for the full narrative, including why this was the only
closure that doesn't reintroduce a name or type heuristic already shown to leak.

Rewrote the existing formatter tests for the new default (non-secret example values now use
`SafeLogValue.Of` explicitly) and added 7 new regressions (plain string/array withheld,
framework-category trust preserved, unknown type still withheld even when trusted,
`SafeLogValue` still yields to name redaction, classified scope value withheld). Re-ran
Seyi's probe unmodified: `PASS classified payloads are excluded` (previously reproduced the
generic-string leak).

**Superseded by round 3 below**: the category-prefix and scope-key-name trust this round
introduced were themselves forgeable, which round 3 found.

## CHANGES_REQUIRED round 3 (QA-03): a logger category string and a scope key name are caller-controlled labels, not identity

Seyi's third re-review (`evidence/BIO-FND-009/codex/REREVIEW-ROUND-3.md`) found that round
2's two "trusted source" mechanisms were themselves forgeable: any application code can
call `ILoggerFactory.CreateLogger("Microsoft.Whatever")` — the category prefix is a string
the caller supplies, not proof the log statement originates from .NET's own framework code
— and any code can push a `BeginScope` dictionary containing a key literally named
`tenant_id`. Both mechanisms trusted a *label*, and a label is exactly what an attacker (or
just careless future code) controls completely. The probe demonstrated the exact same
classified string from round 2, now logged under a forged `"Microsoft.SyntheticApplicationProbe"`
category, rendering verbatim again.

There is no way to make a category string or a scope-dictionary key name non-forgeable —
both are fundamental to how `ILogger` works, not something this codebase's own code
controls end to end. Fixed (`f328577`) by removing both mechanisms entirely rather than
trying to narrow them further: `IsTrustedCategory`, `TrustedCategoryPrefixes`, and
`TrustedScopeKeys` are gone. `Classify()` no longer takes a `trusted` parameter at all — the
**only** way any value is ever rendered now is being an actual `SafeLogValue` instance.
`CorrelationLoggingMiddleware` wraps `correlation_id` and `tenant_id` in `SafeLogValue.Of(...)`
itself, going through the identical opt-in any other call site would use — there is no more
special-cased trust for this module's own middleware either.

**Accepted cost, new and real as of this round**: ASP.NET Core's own framework
request-diagnostics logging (routing, hosting, health-check properties like `EndpointName`)
is now withheld by default too, since Microsoft's source cannot be changed to add a
`SafeLogValue` wrapper. No acceptance criterion requires that richness — AC01 only requires
this module's own `correlation_id`/`tenant_id`, which still render — so AC04's "excluded by
default" was not compromised to keep it. Documented explicitly in `docs/OBSERVABILITY.md`,
including updating the worked example that previously showed framework property values
rendering plainly.

Rewrote 2 tests whose premise (category/scope-key trust) no longer exists into their
opposite (forged-category and unwrapped-trusted-key regressions), added 1 new test
(`SafeLogValue` still shows only a type name for an unknown type - the opt-in vouches for
classification, not for running an arbitrary `ToString()`), and 1 new integration
regression through the real DI-wired provider with a literally forged `"Microsoft."`
category. Re-ran Seyi's probe unmodified: `PASS classified payloads are excluded`
(previously reproduced the forged-category leak).

**Superseded by round 4 below**: an entirely separate code path (custom `TState`) still
bypassed every mechanism this round and QA-01/QA-02 built, which round 4 found.

## CHANGES_REQUIRED round 4 (QA-04): custom log state delegated classified output to the caller's formatter, bypassing Classify entirely

Seyi's fourth re-review (`evidence/BIO-FND-009/codex/REREVIEW-ROUND-4.md`) found that every
fix through round 3 only covered the standard structured-logging shape (a KVP list ending
in `{OriginalFormat}`). A log entry built with a **custom `TState`** - a raw
`ILogger.Log<TState>(logLevel, eventId, state, exception, formatter)` call instead of the
templated `LogInformation`/`LogError`/... extensions - has no such list, so `RenderMessage`
returned `null` and `Write` fell back to calling `logEntry.Formatter(state, exception)`
directly: an arbitrary delegate the CALLER supplied, whose return string never touched
`Classify`, `SensitiveLogKeys`, or `SafeLogValue` at all. The probe's formatter returned a
synthetic biometric value verbatim while every QA-01 through QA-03 structured reproduction
stayed correctly blocked.

Fixed (`518b332`) by never invoking that delegate. `RenderNonStructuredMessage` runs the raw
state through the identical `Classify()` boundary used for every other value in this
formatter: it renders only when the state itself is a `SafeLogValue`, otherwise
`[WITHHELD]` - the same rule, not a separate case.

The review's required closure also named "literal classified message templates" and
malformed templates as needed regression coverage. Investigating this surfaced a distinct,
genuinely unfixable-at-runtime residual risk: C# **string interpolation** (`$"..."`)
resolves a classified value into the `{OriginalFormat}` literal in the CALLER's compiled
code, before any `ILogger` method ever runs - by the time this formatter sees it, there is
no placeholder or argument left to intercept, the same structural limit as QA-02's "a
string has no inherent classification," one level up. There is no runtime fix. Added
`api/Biolog.Api/.editorconfig` promoting .NET's own CA2254 analyzer ("logging message
template should not vary between calls," which also fires on an interpolated template) to
a build error - a compile-time control for a risk with no runtime one, documented as such
in `docs/OBSERVABILITY.md` rather than left unaddressed.

Added 3 unit regressions (custom-state withheld by default, the formatter delegate proven
never invoked via a side-effect flag, `SafeLogValue`-wrapped custom state still renders)
and 1 integration regression with a raw `ILogger.Log<TState>` call through the real
DI-wired provider. Rewrote the one existing test whose premise (calling the caller's
formatter) no longer held. Re-ran Seyi's probe unmodified: `PASS classified payloads are
excluded` (previously reproduced the custom-formatter leak).

## Round 5 preparation (QA-05): full surface enumeration, per god's directive, before resubmitting again

After four straight review rounds each finding a genuinely different bypass of the same
boundary (name blocklist → type-shape allowlist → forgeable trust labels → a separate
`TState` code path), god intervened before a fifth probe-and-patch cycle: enumerate every
path a value can reach an observability sink in this pipeline in one pass, close everything
found, and only resubmit once confident the whole surface is covered — not just whatever
Seyi's last probe happened to find.

Full enumeration (`docs/OBSERVABILITY.md` "Full enumeration of paths to an observability
sink") checked: message template args, string interpolation, custom `TState`/formatters,
`BeginScope` KVP and non-KVP values, `Exception.Message`/`.ToString()`, an `Exception`
logged as an ordinary argument, `Exception.Data`, nested structured values, other
`ILoggerProvider`s, OpenTelemetry span/metric attributes, and ASP.NET Core's
`HttpLogging` middleware.

One real, previously-uncovered gap surfaced: **QA-05 — parallel unredacted logger
providers.** `WebApplication.CreateBuilder` registers `Debug` and `EventSource` providers
by default alongside `Console`. Every registered `ILoggerProvider` receives every log event
independently — neither provider has ever heard of `BiologJsonConsoleFormatter` or
`Classify()`. A value correctly withheld from the console JSON view was still fully visible
via an attached debugger's Output window (`Debug`) or an ETW/EventPipe trace capture
(`EventSource`), regardless of how sound the console formatter's own redaction was — a gap
in the *sink*, not the *boundary*, so nothing QA-01 through QA-04 tested could ever have
caught it.

Fixed (`5391499`): `ObservabilityModule.RegisterServices` now calls `ClearProviders()`
before registering only the redacting console provider. One sink, one boundary — this also
makes `docs/OBSERVABILITY.md`'s pre-existing "no separate viewer to run" claim actually
true, which it was not before this commit.

Two other enumerated paths (`Activity.SetTag`/custom span attributes,
`Microsoft.AspNetCore.HttpLogging`) were confirmed **not currently wired anywhere in this
codebase** (verified by grep, not assumed) and are recorded in the enumeration table as
watched-not-solved for whoever adds them later — they inherit none of this story's
protection automatically.

Added 2 unit regressions for paths that had no dedicated coverage yet (an `Exception`
logged as an ordinary argument withholds by default and shows only its type name even when
explicitly marked safe; `Exception.Data` is confirmed never surfaced, not merely assumed
safe) and 1 integration regression asserting exactly one `ILoggerProvider` is registered.
Did not proactively ask Kemi for a second opinion myself (offered as optional in god's
message) — the full pipeline is within this story's own established scope and the
enumeration is written down in full for anyone to check independently.

## Kemi's design review (independently commissioned by god) confirmed the fix and found one gap in the evidentiary standard

god separately asked Kemi to do her own design review of the whole logging/host-composition
surface before round 6, concurrently with the enumeration work above. Her review
(`conv-fnd009-design`) independently re-verified — not merely trusted from the docs diff —
that: only `ObservabilityModule` touches logging configuration (nothing downstream can
silently re-add a provider after `ClearProviders()`); `.WithLogging()` is never wired, so
OpenTelemetry and the `ILogger` boundary are genuinely separate pipelines; and no
`Activity.SetTag`/`Enrich`/`UseHttpLogging`/`AddHttpLogging` exists anywhere in
`api/Biolog.Api`. She found no sixth path beyond the enumeration table.

One recommendation, not a blocker: the table's two "not applicable today" rows (OpenTelemetry
span/metric attributes, ASP.NET Core `HttpLogging`) were proven by absence-of-code (grep)
alone — the same evidentiary gap Chioma's own QA-05 finding named ("you assumed a path was
safe instead of proving it"), just correct today instead of wrong. Closed in a follow-up
commit (`7662a21`): one integration test sends a real request with a synthetic secret in a
header and asserts it appears nowhere in captured stdout, covering both rows in a single
request. It passes today because neither mechanism is wired; it becomes a failing
regression the moment either one is added without revisiting the table first. Both rows now
carry an executable regression, matching the standard every other row already had.

## Bug caught and fixed before committing: redaction of `properties` alone leaked the value back out through `message`

`BiologJsonConsoleFormatter` originally called the framework's own `logEntry.Formatter`,
which interpolates a message template with the **original, unredacted** argument values
regardless of what the `properties` object showed. Redacting `Password` in `properties`
while `message` still read `"User u_123 authenticated with hunter2"` would have satisfied
none of AC04's intent. Fixed by re-rendering the message from its `{OriginalFormat}`
template myself (`BiologJsonConsoleFormatter.RenderMessage`), substituting each
placeholder's value or `[REDACTED]` in template order, before ever calling the framework
formatter. `BiologJsonConsoleFormatterTests.Write_redacts_sensitive_value_inside_the_rendered_message_itself`
is a named regression test for exactly this. Caught during my own implementation, before
any review — the same "verify what a command actually did, not that it merely ran" habit
recorded from BIO-FND-008.

## New quality gate wired in the same commit, not deferred

`tests/foundation/BIO-FND-008.test.sh` existed for several commits before anyone wired it
into `scripts/quality.sh` (fixed later, commit `3fd805e` on `develop`). `tests/foundation/BIO-FND-009.test.sh`
is wired into `scripts/quality.sh` in the *same* commit that adds it, so it is never a
harness that quietly did nothing.

## governance/stories/BIO-FND-009.json added at builder stage, not deferred to a retroactive fix

BIO-FND-010's traceability inventory (`scripts/check-traceability.py --all`) now treats any
story with a `tests/foundation/<KEY>.test.sh` file as "implemented" and requires a matching
`governance/stories/<KEY>.json` spec to exist — a brand-new repo-wide requirement I closed
retroactively for BIO-FND-007 and BIO-FND-008 earlier today because neither predates it.
This story does not predate it, so the spec is added now instead of manufacturing the exact
gap I just closed twice. It currently declares only the **builder** evidence stage (the only
one that exists yet); `required_acceptance_stages` still lists `["builder","adversarial","acceptance"]`
per the established schema. **Seyi and Chioma still need to add their own
`evidence/BIO-FND-009/{codex,acceptance}/metadata.json` files and the matching `evidence[]`
entries in `governance/stories/BIO-FND-009.json` at their respective stages** — exactly the
per-stage evidence convention already in use, just declared as each stage actually
completes rather than backfilled once. Flagged explicitly here and in the outbox message to
god so this isn't silently assumed. `python3 scripts/check-traceability.py --all` passes
today because inventory-mode validation only requires declared paths to exist, not every
mandatory stage to be present yet; the strict per-story check
(`python3 scripts/check-traceability.py governance/stories/BIO-FND-009.json`) will correctly
report the missing adversarial/acceptance stages until Seyi and Chioma add theirs — that is
expected mid-lifecycle, not a defect.
