#!/usr/bin/env bash
# Static structural validation of infrastructure/docker-compose.local.yml
# against BIO-FND-003-AC01/AC02/AC03/AC04/AC05.
#
# Uses `docker compose config`, which parses and resolves the compose file
# client-side without needing a running Docker daemon - this can (and
# should) run even on a machine where Docker Desktop itself is not started.
# It does NOT start any containers; see scripts/wait-for-infra.sh for the
# live readiness check once the stack is actually running.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

COMPOSE_FILE="infrastructure/docker-compose.local.yml"
fail=0

pass() { echo "PASS  $1"; }
bad()  { echo "FAIL  $1"; fail=1; }

if ! command -v docker >/dev/null 2>&1; then
  echo "validate-infra-compose: docker CLI is required" >&2
  exit 2
fi

if [ ! -f "$COMPOSE_FILE" ]; then
  echo "validate-infra-compose: $COMPOSE_FILE is missing" >&2
  exit 2
fi

default_json="$(docker compose -f "$COMPOSE_FILE" config --format json 2>/dev/null)"
if [ -z "$default_json" ]; then
  bad "docker compose config (default profile) failed to parse $COMPOSE_FILE"
  echo "=============================="
  echo "INFRA COMPOSE VALIDATION: FAILED"
  exit 1
fi
pass "docker compose config parses $COMPOSE_FILE (default profile)"

multi_json="$(docker compose -f "$COMPOSE_FILE" --profile multi-tenant config --format json 2>/dev/null)"
if [ -z "$multi_json" ]; then
  bad "docker compose config --profile multi-tenant failed to parse $COMPOSE_FILE"
else
  pass "docker compose config parses $COMPOSE_FILE (--profile multi-tenant)"
fi

echo "== BIO-FND-003-AC01: control-plane MySQL, tenant MySQL, Redis, RabbitMQ, Azurite, Mailpit =="
python3 -c "
import json, sys
d = json.loads('''$default_json''')
required = {'mysql-control', 'mysql-tenant-asu', 'redis', 'rabbitmq', 'azurite', 'mailpit'}
present = set(d['services'].keys())
missing = required - present
sys.exit(1 if missing else 0)
" && pass "default profile starts all six required services" \
  || bad "default profile is missing one or more of: mysql-control, mysql-tenant-asu, redis, rabbitmq, azurite, mailpit"

echo "== BIO-FND-003-AC02: standard profile starts a second, isolated tenant database =="
python3 -c "
import json, sys
d = json.loads('''$multi_json''')
sys.exit(0 if 'mysql-tenant-grace' in d['services'] else 1)
" && pass "docker compose --profile multi-tenant adds mysql-tenant-grace" \
  || bad "--profile multi-tenant does not add a second tenant database"

python3 -c "
import json, sys
d = json.loads('''$default_json''')
sys.exit(1 if 'mysql-tenant-grace' in d['services'] else 0)
" && pass "mysql-tenant-grace is NOT started by the default profile (opt-in only)" \
  || bad "mysql-tenant-grace is started by default - it must be profile-gated"

echo "== BIO-FND-003-AC03: health checks are defined for every service =="
python3 -c "
import json, sys
d = json.loads('''$multi_json''')
missing = [name for name, svc in d['services'].items() if 'healthcheck' not in svc or not svc['healthcheck'].get('test')]
sys.exit(1 if missing else 0)
" && pass "every service (including the multi-tenant profile) declares a healthcheck" \
  || bad "one or more services have no healthcheck defined"

echo "== BIO-FND-003-AC04: volumes are named =="
python3 -c "
import json, sys
d = json.loads('''$multi_json''')
top_level = set(d.get('volumes', {}).keys())
missing = []
for name, svc in d['services'].items():
    vols = svc.get('volumes', [])
    if not any(v.get('source') in top_level for v in vols if isinstance(v, dict)):
        missing.append(name)
sys.exit(1 if missing else 0)
" && pass "every service mounts a named top-level volume (no anonymous state)" \
  || bad "one or more services have no named volume for their state"

echo "== BIO-FND-003-AC05: no production credentials are required =="
unset_json="$(env -u MYSQL_ROOT_PASSWORD -u RABBITMQ_USER -u RABBITMQ_PASSWORD \
  -u MYSQL_CONTROL_PORT -u MYSQL_TENANT_ASU_PORT -u MYSQL_TENANT_GRACE_PORT \
  -u REDIS_PORT -u RABBITMQ_AMQP_PORT -u RABBITMQ_MGMT_PORT \
  -u AZURITE_BLOB_PORT -u AZURITE_QUEUE_PORT -u MAILPIT_SMTP_PORT -u MAILPIT_UI_PORT \
  docker compose -f "$COMPOSE_FILE" --profile multi-tenant config --format json 2>/dev/null)"
if [ -z "$unset_json" ]; then
  bad "docker compose config fails with no environment overrides set (a required credential has no safe default)"
else
  pass "docker compose config succeeds with zero environment overrides (every credential has a synthetic default)"
fi
python3 -c "
import json, sys
d = json.loads('''$unset_json''')
pw = d['services']['mysql-control']['environment'].get('MYSQL_ROOT_PASSWORD', '')
user = d['services']['rabbitmq']['environment'].get('RABBITMQ_DEFAULT_USER', '')
sys.exit(0 if pw == 'localroot' and user == 'biolog' else 1)
" 2>/dev/null && pass "unset credentials resolve to the documented local-only defaults (localroot / biolog)" \
  || bad "unset credentials did not resolve to the documented synthetic defaults"

echo "=============================="
if [ "$fail" -eq 0 ]; then
  echo "INFRA COMPOSE VALIDATION: SUCCESS"
  exit 0
else
  echo "INFRA COMPOSE VALIDATION: FAILED"
  exit 1
fi
