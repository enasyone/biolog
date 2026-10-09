#!/usr/bin/env bash
# Offline-safe structural checks for BIO-MOB-001 (Flutter Android application
# shell and secure local store). Behavioural assertions (tenant binding,
# encryption passphrase handling, offline mutation modelling, navigation)
# live in the executable Flutter suite under tests/flutter/ (see
# docs/TESTING.md, scripts/quality.sh --full).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

# --- AC01: app boots locally/emulator ---
check "AC01 Flutter app scaffolded at mobile/" test -f mobile/pubspec.yaml
check "AC01 mobile package is named biolog_mobile" bash -c "grep -q '^name: biolog_mobile' mobile/pubspec.yaml"
check "AC01 mobile depends on the Flutter SDK" bash -c "grep -q 'sdk: flutter' mobile/pubspec.yaml"
check "AC01 mobile has a committed lock file" test -f mobile/pubspec.lock
check "AC01 Android platform is scaffolded" test -f mobile/android/app/build.gradle.kts
check "AC01 entry point wires the real store and connectivity signal" \
  bash -c 'grep -q "SqlCipherAppDatabase()" mobile/lib/main.dart && grep -q "PluginConnectivitySignal()" mobile/lib/main.dart'

# --- AC02: sensitive local data is encrypted ---
check "AC02 SQLCipher-backed sqflite dependency declared" bash -c "grep -q 'sqflite_sqlcipher' mobile/pubspec.yaml"
check "AC02 encrypted database implementation exists" test -f mobile/lib/core/db/sqlcipher_app_database.dart
check "AC02 database is opened with a password, not left plaintext" \
  bash -c 'grep -q "password:" mobile/lib/core/db/sqlcipher_app_database.dart \
    && grep -q "passphrase" mobile/lib/core/db/sqlcipher_app_database.dart'
check "AC02 passphrase is Keystore-backed (flutter_secure_storage), not hardcoded" \
  bash -c 'grep -q "flutter_secure_storage" mobile/pubspec.yaml && test -f mobile/lib/core/security/secure_storage_key_value_store.dart'
check "AC02 passphrase is generated at random, not a literal constant" \
  bash -c 'grep -q "Random.secure\|random.nextInt" mobile/lib/core/security/passphrase_vault.dart'
check "AC02 AppDatabase interface is the only local-store surface (fake exists for host tests)" \
  bash -c 'test -f mobile/lib/core/db/app_database.dart && test -f tests/flutter/test/support/fake_app_database.dart'

# --- AC03: tenant context is explicit ---
check "AC03 tenant context model matches the frozen schema shape" test -f mobile/lib/core/tenant/tenant_context.dart
check "AC03 tenant binding rejects a malformed/missing tenant id" \
  bash -c 'grep -q "class TenantBindingError" mobile/lib/core/tenant/tenant_context.dart'
check "AC03 there is no default/fallback tenant constant" \
  bash -c '! grep -rIn "defaultTenant\|DEFAULT_TENANT" mobile/lib 2>/dev/null | grep -q .'
check "AC03 router redirects to tenant-select until a tenant is bound" \
  bash -c 'grep -q "hasBoundTenant" mobile/lib/navigation/app_router.dart'
check "AC03 persisted tenant restore is fully re-validated, not just constructed (BIO-MOB-001-QA-01)" \
  bash -c 'grep -q "_validated" mobile/lib/core/tenant/tenant_context.dart \
    && awk "/factory TenantContext.fromTrustedJson/,/^  }/" mobile/lib/core/tenant/tenant_context.dart | grep -q "_validated" \
    && ! awk "/factory TenantContext.fromTrustedJson/,/^  }/" mobile/lib/core/tenant/tenant_context.dart | grep -q "??"'
check "AC03 bootstrap fails closed on a corrupted persisted tenant record (BIO-MOB-001-QA-01)" \
  bash -c 'grep -q "on TenantBindingError" mobile/lib/core/session/session_controller.dart'
check "AC03 no production path mints a resolved context from caller-supplied fields (BIO-MOB-001-QA-02)" \
  bash -c '! grep -q "factory TenantContext.resolve" mobile/lib/core/tenant/tenant_context.dart \
    && ! grep -v "^\s*//" mobile/lib/core/session/session_controller.dart | grep -q "bindTenant" \
    && ! grep -q "binding-key-field\|Binding key" mobile/lib/features/tenant_select/tenant_select_screen.dart \
    && ! grep -qE "channelsValue: *const *\[.principal.\]" mobile/lib/core/tenant/tenant_context.dart'
check "AC03 tenant-select screen is informational only while unbound (BIO-MOB-001-QA-02)" \
  bash -c '! grep -q "TextFormField" mobile/lib/features/tenant_select/tenant_select_screen.dart \
    && ! grep -q "FilledButton\|ElevatedButton" mobile/lib/features/tenant_select/tenant_select_screen.dart'
check "AC03 a persisted tenant record is trusted only with verified resolution provenance (BIO-MOB-001-QA-03)" \
  bash -c 'grep -q "trusted_resolution_provenance" mobile/lib/core/db/sqlcipher_app_database.dart \
    && grep -q "invalidateLegacyTenantContext" mobile/lib/core/db/app_database.dart \
    && grep -q "invalidateLegacyTenantContext" mobile/lib/core/db/sqlcipher_app_database.dart \
    && grep -q "loadTrustedTenantContext" mobile/lib/core/session/session_controller.dart \
    && ! grep -q "return TenantContext.fromTrustedJson(json)" mobile/lib/core/db/sqlcipher_app_database.dart'
check "AC03 the real store cannot be used to self-issue trusted provenance (BIO-MOB-001-QA-04)" \
  bash -c 'awk "/Future<void> saveTenantContext/,/^  }/" mobile/lib/core/db/sqlcipher_app_database.dart | grep -q "UnimplementedError" \
    && ! awk "/Future<void> saveTenantContext/,/^  }/" mobile/lib/core/db/sqlcipher_app_database.dart | grep -q "_requireOpen"'

# --- AC04: navigation supports enrolment, verify, QR/barcode, bus and offline states ---
check "AC04 configured nav items cover every required state" \
  bash -c 'grep -q "id: .enrolment." mobile/lib/navigation/nav_items.dart \
    && grep -q "id: .verify." mobile/lib/navigation/nav_items.dart \
    && grep -q "id: .scan." mobile/lib/navigation/nav_items.dart \
    && grep -q "id: .bus." mobile/lib/navigation/nav_items.dart \
    && grep -q "id: .offline." mobile/lib/navigation/nav_items.dart'
check "AC04 router is built from the configured nav items, not ad-hoc routes" \
  bash -c 'grep -q "configuredNavItems" mobile/lib/navigation/app_router.dart'
check "AC04 offline state has a visible, dedicated banner" test -f mobile/lib/features/offline/offline_banner.dart
check "AC04 offline queue depth is surfaced, not silently dropped" \
  bash -c 'grep -q "pendingMutationCount" mobile/lib/core/session/session_controller.dart'
check "AC04 offline mutation queue mirrors the frozen envelope schema" \
  bash -c 'grep -q "mutation_id" mobile/lib/core/offline/offline_mutation.dart \
    && grep -q "sync_state" mobile/lib/core/offline/offline_mutation.dart'
check "AC04 a reused idempotency key never silently replaces the queued mutation (BIO-MOB-001-F01)" \
  bash -c 'grep -q "class IdempotencyConflictError" mobile/lib/core/offline/idempotent_enqueue.dart \
    && grep -q "resolveEnqueue" mobile/lib/core/db/sqlcipher_app_database.dart \
    && awk "/Future<OfflineMutation> enqueueMutation/,/^  }/" mobile/lib/core/db/sqlcipher_app_database.dart | grep -q "ConflictAlgorithm.abort" \
    && ! awk "/Future<OfflineMutation> enqueueMutation/,/^  }/" mobile/lib/core/db/sqlcipher_app_database.dart | grep -q "ConflictAlgorithm.replace"'
check "AC04 the enqueue idempotency check is transactional (closes the concurrency gap)" \
  bash -c 'grep -q "_requireOpen.transaction" mobile/lib/core/db/sqlcipher_app_database.dart'

# --- AC05: no production data is bundled ---
check "AC05 no local.properties (machine-specific SDK path) is tracked" \
  bash -c '! git ls-files mobile/android/local.properties | grep -q .'
check "AC05 build/dependency-state output is gitignored" \
  bash -c "grep -q 'mobile/.dart_tool/' .gitignore && grep -q 'mobile/build/' .gitignore"
check "AC05 no keystore/signing material is tracked" \
  bash -c '! git ls-files mobile | grep -qE "\.jks$|\.keystore$|key\.properties$"'
check "AC05 sample tenant ids in source are documentation-only synthetic examples" \
  bash -c '! grep -rIn "PRODUCTION\|prod_tenant" mobile/lib 2>/dev/null | grep -q .'

# --- Developer test wiring ---
check "Flutter test package depends on the mobile app for logic tests" \
  bash -c 'grep -q "biolog_mobile" tests/flutter/pubspec.yaml'
check "unit/widget tests mapped to BIO-MOB-001 ACs exist" \
  bash -c 'test -f tests/flutter/test/bio_mob_001_ac01_app_boot_test.dart \
    && test -f tests/flutter/test/bio_mob_001_ac02_passphrase_vault_test.dart \
    && test -f tests/flutter/test/bio_mob_001_ac03_session_controller_test.dart \
    && test -f tests/flutter/test/bio_mob_001_ac03_persisted_tenant_validation_test.dart \
    && test -f tests/flutter/test/bio_mob_001_ac03_save_tenant_context_unimplemented_test.dart \
    && test -f tests/flutter/test/bio_mob_001_ac04_nav_items_test.dart \
    && test -f tests/flutter/test/bio_mob_001_offline_mutation_test.dart \
    && test -f tests/flutter/test/bio_mob_001_ac04_offline_queue_idempotency_test.dart \
    && test -f tests/flutter/test/bio_mob_001_identifiers_clock_test.dart'
check "builder evidence recorded" test -d evidence/BIO-MOB-001/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
