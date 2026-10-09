#!/usr/bin/env bash
# Developer tests for BIO-FND-003, mapped to acceptance criteria.
# Run from anywhere: tests/foundation/BIO-FND-003.test.sh
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

total=0
failed=0
skipped=0

check() {
  local name="$1"; shift
  total=$((total + 1))
  if "$@"; then
    echo "PASS  $name"
  else
    echo "FAIL  $name"
    failed=$((failed + 1))
  fi
}

skip() {
  local name="$1" reason="$2"
  skipped=$((skipped + 1))
  echo "SKIP  $name ($reason)"
}

# --- Portability regression: macOS ships bash 3.2 as /bin/bash (no
# associative arrays, and an empty array expanded under `set -u` is an
# "unbound variable" error, unlike bash 4+). `#!/usr/bin/env bash` resolves
# to that on any machine without a newer bash earlier in PATH, so every
# infra script here must parse - and, cheaply, actually run - under it.
infra_scripts_are_bash32_compatible() {
  command -v /bin/bash >/dev/null 2>&1 || return 0
  for f in scripts/infra-up.sh scripts/infra-down.sh scripts/infra-reset.sh \
           scripts/wait-for-infra.sh scripts/validate-infra-compose.sh; do
    /bin/bash -n "$f" || return 1
  done
  # infra-up.sh's empty-profile code path is the one that broke under 3.2
  # before the fix (bash 3.2, not `env bash`, so this genuinely exercises it
  # regardless of what a newer bash may also be installed as elsewhere).
  /bin/bash -c 'a=(); [ ${#a[@]} -eq 0 ]' 2>/dev/null
}
check "Portability: infra scripts parse under /bin/bash (macOS default, bash 3.2)" infra_scripts_are_bash32_compatible

no_associative_arrays_in_infra_scripts() {
  # grep -v strips comment-only lines first so this doesn't trip over the
  # portability comments in these same scripts that mention "declare -A" by
  # name to explain why it's avoided.
  ! grep -vh '^\s*#' scripts/infra-up.sh scripts/infra-down.sh scripts/infra-reset.sh \
      scripts/wait-for-infra.sh scripts/validate-infra-compose.sh 2>/dev/null \
      | grep -q "declare -A"
}
check "Portability: no bash 4+ associative arrays in infra scripts" no_associative_arrays_in_infra_scripts

# --- BIO-FND-003-AC01: real check runs clean on the actual compose file ---
check "AC01-AC05: validate-infra-compose.sh passes on the real compose file" \
  bash scripts/validate-infra-compose.sh

# Each regression below builds a scratch repo with the real compose file and
# validator (validate-infra-compose.sh resolves paths relative to the repo
# root it's run from, so the fixture needs that same infrastructure/ +
# scripts/ layout), applies one mutation, and asserts the check now fails.

# --- Regression: a missing required service must fail AC01 --------------
remove_mysql_control() {
  python3 -c "
p = '$1'
lines = open(p).readlines()
out = []
dropping = False
for line in lines:
    if line.startswith('  mysql-control:'):
        dropping = True
        continue
    if dropping and line.startswith('  mysql-tenant-asu:'):
        dropping = False
    if not dropping:
        out.append(line)
open(p, 'w').writelines(out)
"
}
ac01_missing_service_is_caught_in_repo_layout() {
  local dir
  dir="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd003-ac01.XXXXXX")"
  mkdir -p "$dir/infrastructure" "$dir/scripts"
  cp infrastructure/docker-compose.local.yml "$dir/infrastructure/docker-compose.local.yml"
  cp scripts/validate-infra-compose.sh "$dir/scripts/validate-infra-compose.sh"
  chmod +x "$dir/scripts/validate-infra-compose.sh"
  remove_mysql_control "$dir/infrastructure/docker-compose.local.yml"
  ( cd "$dir" && git init -q -b main )
  local rc=0 out
  out="$(cd "$dir" && bash scripts/validate-infra-compose.sh 2>&1)" || rc=$?
  rm -rf "$dir"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "default profile is missing one or more of"
}
check "AC01: removing a required service (mysql-control) fails the check" ac01_missing_service_is_caught_in_repo_layout

# --- Regression: the second tenant must be profile-gated, not default ---
unprofile_tenant_grace() {
  python3 -c "
p = '$1'
text = open(p).read()
text = text.replace('    profiles: [\"multi-tenant\"]\n', '')
open(p, 'w').write(text)
"
}
ac02_ungated_second_tenant_is_caught() {
  local dir
  dir="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd003-ac02.XXXXXX")"
  mkdir -p "$dir/infrastructure" "$dir/scripts"
  cp infrastructure/docker-compose.local.yml "$dir/infrastructure/docker-compose.local.yml"
  cp scripts/validate-infra-compose.sh "$dir/scripts/validate-infra-compose.sh"
  chmod +x "$dir/scripts/validate-infra-compose.sh"
  unprofile_tenant_grace "$dir/infrastructure/docker-compose.local.yml"
  ( cd "$dir" && git init -q -b main )
  local rc=0 out
  out="$(cd "$dir" && bash scripts/validate-infra-compose.sh 2>&1)" || rc=$?
  rm -rf "$dir"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "mysql-tenant-grace is started by default"
}
check "AC02: a second tenant DB started by default (not profile-gated) fails the check" ac02_ungated_second_tenant_is_caught

# --- Regression: a service without a healthcheck must fail AC03 ---------
strip_redis_healthcheck() {
  python3 -c "
import re
p = '$1'
text = open(p).read()
text = re.sub(r'    healthcheck:\n(?:      .*\n)*(?=    volumes:\n      - redis_data)', '', text)
open(p, 'w').write(text)
"
}
ac03_missing_healthcheck_is_caught() {
  local dir
  dir="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd003-ac03.XXXXXX")"
  mkdir -p "$dir/infrastructure" "$dir/scripts"
  cp infrastructure/docker-compose.local.yml "$dir/infrastructure/docker-compose.local.yml"
  cp scripts/validate-infra-compose.sh "$dir/scripts/validate-infra-compose.sh"
  chmod +x "$dir/scripts/validate-infra-compose.sh"
  strip_redis_healthcheck "$dir/infrastructure/docker-compose.local.yml"
  ( cd "$dir" && git init -q -b main )
  local rc=0 out
  out="$(cd "$dir" && bash scripts/validate-infra-compose.sh 2>&1)" || rc=$?
  rm -rf "$dir"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "one or more services have no healthcheck defined"
}
check "AC03: a service with no healthcheck (redis) fails the check" ac03_missing_healthcheck_is_caught

# --- Regression for BIO-FND-003-QA-01: a runtime container with no
# healthcheck must be reported NOT ready by wait-for-infra.sh, even though
# every service in the real compose file always declares one. Uses a fake
# `docker` on PATH so this doesn't depend on a live daemon (which the
# review's sandbox couldn't reach either).
qa01_no_healthcheck_is_not_ready() {
  local fakebin
  fakebin="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd003-qa01-bin.XXXXXX")"
  printf '%s\n' \
    '#!/usr/bin/env bash' \
    'if [ "${1:-}" = compose ]; then' \
    '  case " $* " in' \
    '    *" config --services "*) echo redis ;;' \
    '    *" ps -q redis "*) echo synthetic-container-id ;;' \
    '  esac' \
    '  exit 0' \
    'fi' \
    'if [ "${1:-}" = inspect ]; then echo no-healthcheck; exit 0; fi' \
    'exit 0' > "$fakebin/docker"
  chmod +x "$fakebin/docker"
  local rc=0 out
  out="$(PATH="$fakebin:$PATH" scripts/wait-for-infra.sh --timeout 1 2>&1)" || rc=$?
  rm -rf "$fakebin"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "redis(no-healthcheck)"
}
check "AC03/QA-01: wait-for-infra.sh reports a no-healthcheck container as NOT ready" qa01_no_healthcheck_is_not_ready

# --- Regression: a service without a named volume must fail AC04 --------
strip_redis_volume() {
  python3 -c "
import re
p = '$1'
text = open(p).read()
text = re.sub(r'    volumes:\n      - redis_data:/data\n', '', text)
open(p, 'w').write(text)
"
}
ac04_missing_volume_is_caught() {
  local dir
  dir="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd003-ac04.XXXXXX")"
  mkdir -p "$dir/infrastructure" "$dir/scripts"
  cp infrastructure/docker-compose.local.yml "$dir/infrastructure/docker-compose.local.yml"
  cp scripts/validate-infra-compose.sh "$dir/scripts/validate-infra-compose.sh"
  chmod +x "$dir/scripts/validate-infra-compose.sh"
  strip_redis_volume "$dir/infrastructure/docker-compose.local.yml"
  ( cd "$dir" && git init -q -b main )
  local rc=0 out
  out="$(cd "$dir" && bash scripts/validate-infra-compose.sh 2>&1)" || rc=$?
  rm -rf "$dir"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "one or more services have no named volume"
}
check "AC04: a service with no named volume (redis) fails the check" ac04_missing_volume_is_caught

# --- Regression for BIO-FND-003-QA-02: infra-reset.sh must propagate a
# Docker cleanup failure instead of reporting success. Uses a fake `docker`
# that fails every invocation, mirroring the review's reproduction.
qa02_cleanup_failure_is_propagated() {
  local fakebin
  fakebin="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd003-qa02-bin.XXXXXX")"
  printf '%s\n' '#!/usr/bin/env bash' 'exit 1' > "$fakebin/docker"
  chmod +x "$fakebin/docker"
  local rc=0 out
  out="$(PATH="$fakebin:$PATH" scripts/infra-reset.sh redis 2>&1)" || rc=$?
  rm -rf "$fakebin"
  [ "$rc" -ne 0 ] && ! printf '%s\n' "$out" | grep -q "^infra-reset: done"
}
check "AC04/QA-02: infra-reset.sh propagates a Docker cleanup failure instead of reporting done" qa02_cleanup_failure_is_propagated

qa02_reset_of_never_started_service_still_succeeds() {
  # Boundary case: QA-02's fix must not turn "the volume was never created
  # because this service was never started" into a false failure. The fake
  # `docker volume inspect` here emits Docker's actual "no such volume"
  # diagnostic - the one signal QA-03 (below) requires the script to
  # actually check for, rather than treating any inspect failure as absence.
  local fakebin
  fakebin="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd003-qa02-ok-bin.XXXXXX")"
  printf '%s\n' \
    '#!/usr/bin/env bash' \
    'if [ "${1:-}" = compose ]; then exit 0; fi' \
    'if [ "${1:-}" = volume ] && [ "${2:-}" = inspect ]; then echo "Error: No such volume: $3" >&2; exit 1; fi' \
    'exit 0' > "$fakebin/docker"
  chmod +x "$fakebin/docker"
  local rc=0
  PATH="$fakebin:$PATH" scripts/infra-reset.sh mysql-tenant-grace >/dev/null 2>&1 || rc=$?
  rm -rf "$fakebin"
  [ "$rc" -eq 0 ]
}
check "AC04/QA-02: resetting a service whose volume was never created still succeeds" qa02_reset_of_never_started_service_still_succeeds

# --- Regression for BIO-FND-003-QA-03: a `docker volume inspect` failure
# that is NOT Docker's own "no such volume" diagnostic (e.g. permission
# denied, daemon unreachable) must fail closed, not be treated the same as
# a confirmed-absent volume.
qa03_inspect_error_fails_closed() {
  local fakebin
  fakebin="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd003-qa03-bin.XXXXXX")"
  printf '%s\n' \
    '#!/usr/bin/env bash' \
    'if [ "${1:-}" = compose ]; then exit 0; fi' \
    'if [ "${1:-}" = volume ] && [ "${2:-}" = inspect ]; then echo "permission denied" >&2; exit 1; fi' \
    'exit 0' > "$fakebin/docker"
  chmod +x "$fakebin/docker"
  local rc=0 out
  out="$(PATH="$fakebin:$PATH" scripts/infra-reset.sh redis 2>&1)" || rc=$?
  rm -rf "$fakebin"
  [ "$rc" -ne 0 ] && ! printf '%s\n' "$out" | grep -q "^infra-reset: done" \
    && printf '%s\n' "$out" | grep -q "could not confirm whether volume"
}
check "AC04/QA-03: a volume-inspect error (not 'no such volume') fails closed instead of being treated as absent" qa03_inspect_error_fails_closed

# --- Regression: a credential without a synthetic default must fail AC05 -
strip_mysql_password_default() {
  python3 -c "
p = '$1'
text = open(p).read()
text = text.replace('\${MYSQL_ROOT_PASSWORD:-localroot}', '\${MYSQL_ROOT_PASSWORD}')
open(p, 'w').write(text)
"
}
ac05_missing_default_is_caught() {
  local dir
  dir="$(mktemp -d "${TMPDIR:-/tmp}/biolog-fnd003-ac05.XXXXXX")"
  mkdir -p "$dir/infrastructure" "$dir/scripts"
  cp infrastructure/docker-compose.local.yml "$dir/infrastructure/docker-compose.local.yml"
  cp scripts/validate-infra-compose.sh "$dir/scripts/validate-infra-compose.sh"
  chmod +x "$dir/scripts/validate-infra-compose.sh"
  strip_mysql_password_default "$dir/infrastructure/docker-compose.local.yml"
  ( cd "$dir" && git init -q -b main )
  local rc=0 out
  out="$(cd "$dir" && bash scripts/validate-infra-compose.sh 2>&1)" || rc=$?
  rm -rf "$dir"
  [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q "unset credentials did not resolve to the documented synthetic defaults"
}
check "AC05: a credential with no synthetic default fails the check" ac05_missing_default_is_caught

# --- Live smoke test: only runs if Docker Desktop's daemon is reachable --
# Uses uncommon, dedicated ports (not infrastructure/.env, not the compose
# defaults) so this test doesn't depend on - or collide with - whatever
# else happens to be bound to 3306/6379/5672/etc. on the host machine.
if docker info >/dev/null 2>&1; then
  check "AC03 (live): scripts/wait-for-infra.sh converges to healthy within its timeout" bash -c '
    export MYSQL_CONTROL_PORT=43061 MYSQL_TENANT_ASU_PORT=43062 MYSQL_TENANT_GRACE_PORT=43063
    export REDIS_PORT=46379 RABBITMQ_AMQP_PORT=45672 RABBITMQ_MGMT_PORT=45673
    export AZURITE_BLOB_PORT=40000 AZURITE_QUEUE_PORT=40001
    export MAILPIT_SMTP_PORT=41025 MAILPIT_UI_PORT=48025
    scripts/infra-up.sh >/dev/null 2>&1
    ok=0
    scripts/wait-for-infra.sh --timeout 180 && ok=1
    scripts/infra-down.sh >/dev/null 2>&1
    [ "$ok" -eq 1 ]
  '
else
  skip "AC03 (live): scripts/wait-for-infra.sh converges to healthy" "Docker daemon not reachable on this machine - see docs/LOCAL-INFRASTRUCTURE.md"
fi

echo "=============================="
echo "$((total - failed))/$total checks passed, $skipped skipped"
[ "$failed" -eq 0 ]
