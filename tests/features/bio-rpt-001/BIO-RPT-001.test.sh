#!/usr/bin/env bash
# Offline-safe structural checks for BIO-RPT-001 (notification event/rule foundation).
# Behavioural assertions live in the executable xunit suites under tests/features/bio-rpt-001/
# and tests/dotnet/Biolog.IntegrationTests/NotificationEndpointsTests.cs (see
# docs/features/bio-rpt-001/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-rpt-001/Biolog.Features.Notifications

# --- AC01: rules subscribe to versioned domain events ---
check "AC01 feature project exists" test -f "$FEATURE.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE.csproj'"
check "AC01 feature project has no ProjectReference to Biolog.Api.csproj" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\\.Api\\.csproj' '$FEATURE.csproj'"
check "AC01 the event envelope mirrors the BIO-ARC-003 schema's required fields" bash -c "
  for f in EventId SchemaVersion TenantId AggregateType AggregateId AggregateVersion CorrelationId CausationId OccurredAt ReceivedAt Producer PayloadClassification Payload; do
    grep -q \"\$f\" '$FEATURE/Domain/DomainEventEnvelope.cs' || exit 1
  done"
check "AC01 a pub/sub bus is the subscription mechanism" test -f "$FEATURE/Domain/IDomainEventBus.cs"
check "AC01 a rule declares the schema-version range it understands" \
  bash -c "grep -q 'MinSchemaVersion' '$FEATURE/Domain/NotificationRule.cs' && grep -q 'MaxSchemaVersion' '$FEATURE/Domain/NotificationRule.cs'"
check "AC01 matching excludes an out-of-range schema version, not just a different aggregate type" \
  bash -c "grep -q 'SupportsSchemaVersion' '$FEATURE/Domain/INotificationRuleRepository.cs'"

# --- AC02: channel adapters are separated ---
check "AC02 one adapter file exists per named channel" bash -c "
  test -f '$FEATURE/Adapters/EmailChannelAdapter.cs' &&
  test -f '$FEATURE/Adapters/SmsChannelAdapter.cs' &&
  test -f '$FEATURE/Adapters/PushChannelAdapter.cs' &&
  test -f '$FEATURE/Adapters/TeamsChannelAdapter.cs' &&
  test -f '$FEATURE/Adapters/WebhookChannelAdapter.cs'"
check "AC02 every adapter implements the one typed interface" \
  bash -c "grep -l ': INotificationChannelAdapter' '$FEATURE'/Adapters/*.cs | wc -l | grep -q '^ *5$'"
check "AC02 the dispatcher never branches on NotificationChannel itself (dictionary dispatch only)" \
  bash -c "! grep -qE 'case NotificationChannel\.|== NotificationChannel\.' '$FEATURE/Domain/NotificationDispatchService.cs'"

# --- AC03: retry/deduplication and delivery audit exist ---
check "AC03 a delivery audit sink exists" test -f "$FEATURE/Domain/INotificationDeliveryAuditSink.cs"
check "AC03 dedup checks (tenant, event, rule, channel) before sending" \
  bash -c "grep -q 'HasSucceededAsync' '$FEATURE/Domain/NotificationDispatchService.cs'"
check "AC03 retry is bounded, not infinite" bash -c "grep -q 'MaxAttempts' '$FEATURE/Domain/NotificationDispatchService.cs'"
check "AC03 a permanent failure is never retried" \
  bash -c "grep -q 'ChannelDeliveryOutcome.PermanentFailure' '$FEATURE/Domain/NotificationDispatchService.cs'"

# --- AC04: sensitive information is minimized per channel ---
check "AC04 an allowlist projection exists (never a denylist)" test -f "$FEATURE/Domain/PayloadAllowlist.cs"
check "AC04 personal/sensitive classifications short-circuit to an empty projection" \
  bash -c "grep -q 'EventClassification.Personal or EventClassification.Sensitive' '$FEATURE/Domain/PayloadAllowlist.cs'"
check "AC04 SMS's allowlist is strictly narrower than Email's" bash -c "
  sms_count=\$(grep -c '\"' '$FEATURE/Adapters/SmsChannelAdapter.cs' | head -1)
  grep -q 'AllowedKeys = \[\"reference\"\]' '$FEATURE/Adapters/SmsChannelAdapter.cs' &&
  grep -q 'AllowedKeys = \[\"summary\", \"action\", \"reference\"\]' '$FEATURE/Adapters/EmailChannelAdapter.cs'"

# --- AC05: /reports, /audit, /dashboard connected via ReportingService or documented HYBRID ---
check "AC05 the primary-action endpoint matches web/features/reporting/live.ts's exact path" \
  bash -c "grep -q '/primary-action' api/Biolog.Api/Modules/BioRpt001Module.cs && grep -q \"reporting/primary-action\" web/features/reporting/live.ts"
check "AC05 the view-audit-trail endpoint matches web/features/reporting/live.ts's exact path" \
  bash -c "grep -q '/view-audit-trail' api/Biolog.Api/Modules/BioRpt001Module.cs && grep -q \"reporting/view-audit-trail\" web/features/reporting/live.ts"
# Pinned to BIO-RPT-001's own merge range (base fd54e6e -> tip 16759df), not `develop...`: a live
# diff here would wrongly fail any later domain story (already happened for BIO-RPT-002, which
# legitimately extended web/features/reporting - see ports.ts's own doc comment) - this is a
# historical assertion about BIO-RPT-001's own commits, not a permanent ban on the reporting port
# surface (same class of bug as BIO-INT-001's identical check, found 2026-10-06).
check "BIO-RPT-001's own branch did not modify BIO-UX-005/011's owned reporting UI" \
  bash -c "! git diff --name-only fd54e6ecd5f5d7630775c06210022e7c878524b2..16759dff7b531eb294e0a6a53d87b37beab5112e -- web/features/reporting web/app/reports web/app/audit 2>/dev/null | grep -q ."
check "AC05 the live-connection design is documented" bash -c "grep -qi 'AC05' docs/features/bio-rpt-001/README.md"

# --- Traceability ---
check "builder evidence directory exists" test -d evidence/BIO-RPT-001/builder
check "feature docs exist" test -f docs/features/bio-rpt-001/README.md

echo "----"
echo "PASS=$pass FAIL=$fail"
[ "$fail" -eq 0 ]
