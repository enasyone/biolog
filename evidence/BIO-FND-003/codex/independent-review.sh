#!/usr/bin/env bash
set -u

repo="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d "${TMPDIR:-/tmp}/bio-fnd-003-codex.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT
fail=0

pass() { printf 'PASS %s\n' "$1"; }
fail_case() { printf 'FAIL %s\n' "$1"; fail=1; }

cd "$repo"
if scripts/validate-infra-compose.sh >/dev/null; then pass 'AC01-05 Compose structural validation'; else fail_case 'AC01-05 Compose structural validation'; fi

default_json="$(docker compose -f infrastructure/docker-compose.local.yml config --format json)"
multi_json="$(docker compose -f infrastructure/docker-compose.local.yml --profile multi-tenant config --format json)"
if DEFAULT_JSON="$default_json" MULTI_JSON="$multi_json" python3 - <<'PY'
import json, os
d=json.loads(os.environ['DEFAULT_JSON']); m=json.loads(os.environ['MULTI_JSON'])
required={'mysql-control','mysql-tenant-asu','redis','rabbitmq','azurite','mailpit'}
assert required <= set(d['services'])
assert 'mysql-tenant-grace' not in d['services']
assert 'mysql-tenant-grace' in m['services']
asu=m['services']['mysql-tenant-asu']; grace=m['services']['mysql-tenant-grace']
assert asu['environment']['MYSQL_DATABASE'] != grace['environment']['MYSQL_DATABASE']
assert asu['volumes'][0]['source'] != grace['volumes'][0]['source']
assert asu['ports'][0]['published'] != grace['ports'][0]['published']
for service in m['services'].values():
    assert service.get('healthcheck',{}).get('test')
    assert any(isinstance(v,dict) and v.get('type')=='volume' for v in service.get('volumes',[]))
PY
then pass 'AC01-04 service topology, tenant separation, healthchecks and named volumes'; else fail_case 'AC01-04 service topology, tenant separation, healthchecks and named volumes'; fi

if ! git grep -nE '/(Users|home)/[^/]+/' -- ':!scripts/validate-bootstrap.sh' ':!evidence/BIO-FND-001/builder/SUMMARY.md' ':!evidence/BIO-FND-003/codex/*' >/dev/null \
   && scripts/secret-scan.sh --all >/dev/null; then
  pass 'AC05 no tracked machine paths or detected secrets'
else
  fail_case 'AC05 no tracked machine paths or detected secrets'
fi

mkdir -p "$tmp/bin"
printf '%s\n' '#!/usr/bin/env bash' \
  'if [ "${FAKE_DOCKER_MODE:-}" = fail ]; then exit 1; fi' \
  'if [ "${FAKE_DOCKER_MODE:-}" = inspect-error ] && [ "${1:-}" = volume ] && [ "${2:-}" = inspect ]; then echo "permission denied" >&2; exit 1; fi' \
  'if [ "${1:-}" = compose ]; then' \
  '  case " $* " in' \
  '    *" config --services "*) echo redis ;;' \
  '    *" ps -q redis "*) echo synthetic-container-id ;;' \
  '  esac' \
  '  exit 0' \
  'fi' \
  'if [ "${1:-}" = inspect ]; then echo no-healthcheck; exit 0; fi' \
  'exit 0' >"$tmp/bin/docker"
chmod +x "$tmp/bin/docker"

# A readiness gate must never treat a container without a healthcheck as ready.
if PATH="$tmp/bin:$PATH" scripts/wait-for-infra.sh --timeout 1 >/dev/null 2>&1; then
  fail_case 'AC03 wait rejects a service with no healthcheck'
else
  pass 'AC03 wait rejects a service with no healthcheck'
fi

# Targeted reset must propagate Docker failures rather than claim cleanup succeeded.
if FAKE_DOCKER_MODE=fail PATH="$tmp/bin:$PATH" scripts/infra-reset.sh redis >/dev/null 2>&1; then
  fail_case 'AC04 targeted reset reports Docker cleanup failure'
else
  pass 'AC04 targeted reset reports Docker cleanup failure'
fi

# A failed volume inspection is not equivalent to a confirmed absent volume.
if FAKE_DOCKER_MODE=inspect-error PATH="$tmp/bin:$PATH" scripts/infra-reset.sh redis >/dev/null 2>&1; then
  fail_case 'AC04 targeted reset distinguishes inspect failure from absent volume'
else
  pass 'AC04 targeted reset distinguishes inspect failure from absent volume'
fi

# Invalid service names and invalid readiness flags must fail deterministically.
if PATH="$tmp/bin:$PATH" scripts/infra-reset.sh unknown-service >/dev/null 2>&1; then fail_case 'AC04 unknown reset target fails'; else pass 'AC04 unknown reset target fails'; fi
if PATH="$tmp/bin:$PATH" scripts/wait-for-infra.sh --bogus >/dev/null 2>&1; then fail_case 'AC03 unknown wait flag fails'; else pass 'AC03 unknown wait flag fails'; fi

exit "$fail"
