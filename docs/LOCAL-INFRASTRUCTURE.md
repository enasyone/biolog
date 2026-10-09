# BIOLOG local Docker infrastructure (BIO-FND-003)

`infrastructure/docker-compose.local.yml` provisions every local service the
platform needs so it can be built and tested without Azure or GitHub
(BIO-FND-003 user story), per
`02-ARCHITECTURE/MASTER-TECHNICAL-SPECIFICATION.md` section 1 (Stack):
isolated control-plane and tenant MySQL, Redis/RabbitMQ (local stand-ins for
the Azure-native messaging/cache adapters used in production), Azurite
(Blob Storage emulator), and Mailpit (local SMTP + web UI for verifying
outbound email without a real mail provider).

## Services and ports (AC01)

| Service | Image | Default port(s) | Purpose |
|---|---|---|---|
| `mysql-control` | `mysql:8.4` | `33061` | Control-plane DB: tenant config, licensing, routing |
| `mysql-tenant-asu` | `mysql:8.4` | `33062` | First tenant's operational data (started by default) |
| `mysql-tenant-grace` | `mysql:8.4` | `33063` | Second, isolated tenant - `multi-tenant` profile only (AC02) |
| `redis` | `redis:7-alpine` | `6379` | Cache/session store, AOF-persisted |
| `rabbitmq` | `rabbitmq:3-management-alpine` | `5672` (AMQP), `15672` (UI) | Messaging |
| `azurite` | `mcr.microsoft.com/azure-storage/azurite` | `10000` (blob), `10001` (queue) | Blob Storage emulator |
| `mailpit` | `axllent/mailpit` | `1025` (SMTP), `8025` (UI) | Captures outbound mail locally |

All ports and credentials are overridable via `infrastructure/.env` (copy
from `infrastructure/.env.example`); every one of them has a synthetic
local-only default baked into the compose file, so the stack starts with
**zero required configuration** (AC05 - no production credentials needed).

## Bringing the stack up (AC01, AC02, AC03)

```sh
scripts/infra-up.sh                     # control plane + first tenant only
scripts/infra-up.sh --multi-tenant      # + second tenant, for cross-tenant isolation testing
scripts/infra-up.sh --wait              # either of the above, then block until every
                                         # started service reports healthy
```

Every service declares a Docker healthcheck (`mysqladmin ping`, `redis-cli
ping`, `rabbitmq-diagnostics -q ping`, a TCP connect check via Node for
Azurite, and an HTTP probe for Mailpit). `scripts/wait-for-infra.sh` polls
`docker inspect`'s `State.Health.Status` for every service resolved by the
active profile and only returns once all of them are `healthy` (or times out
after 180s by default, `--timeout SECONDS` to change it) - this is what
"bootstrap waits for readiness" means for AC03: a script that actually
blocks on the health signal, not a fixed sleep.

## Tearing down and resetting (AC04)

```sh
scripts/infra-down.sh                          # stop containers, keep all data
scripts/infra-reset.sh                         # stop + delete every named volume
scripts/infra-reset.sh mysql-tenant-asu redis  # stop + delete only these services' volumes
```

Every service's state lives in exactly one named volume
(`biolog_mysql_control`, `biolog_mysql_tenant_asu`, `biolog_mysql_tenant_grace`,
`biolog_redis_data`, `biolog_rabbitmq_data`, `biolog_azurite_data`,
`biolog_mailpit_data`) - there are no anonymous volumes to hunt for.
`infra-reset.sh` with no arguments wipes all of them for a clean-slate
bootstrap; with service names, it wipes only those, leaving the rest of the
stack (and its data) running. The compose file pins `name: biolog` at the
top level specifically so these volume names stay exactly this - without
it, Compose derives the project name (and therefore the volume name prefix)
from the containing directory, which would silently break `infra-reset.sh`'s
hardcoded lookups if the repository were ever checked out under a
differently-named path.

## Validating without Docker Desktop running (AC01-AC05)

```sh
scripts/validate-infra-compose.sh
```

Runs `docker compose config` (both the default and `--profile multi-tenant`
resolutions) and asserts every AC structurally: the required six services
exist by default, the second tenant is profile-gated rather than default,
every service has a healthcheck, every service has a named volume, and the
compose file resolves successfully with zero environment variables set
(proving no credential is actually required). This needs the Docker CLI but
**not** a running daemon, so it works even before Docker Desktop is started.

## Live verification performed for this story

Docker Desktop's daemon was not running at the start of this bootstrap, but
was started during development and the full stack was verified live, not
just statically:

- `scripts/infra-up.sh --multi-tenant --wait` pulled all six images and
  brought up all seven services (default profile + `mysql-tenant-grace`);
  every one converged to Docker-reported `healthy`.
- Cross-tenant isolation (AC02) was confirmed directly: a row written into
  `mysql-tenant-asu`'s `biolog_asu` database is not visible from
  `mysql-tenant-grace` (separate MySQL server instances, separate volumes -
  there is no shared database to leak through).
- `scripts/infra-reset.sh redis` and the no-argument full reset were each
  run against live containers and volumes and confirmed to remove exactly
  the targeted volume(s), leaving the rest of the stack running.
- Two real defects were only caught by this live run and are already fixed
  in the committed compose file:
  1. The Azurite healthcheck's Node one-liner assumed the `'connect'` event
     listener receives the socket as an argument; it doesn't, so every
     healthcheck attempt threw `TypeError: Cannot read properties of
     undefined`. Fixed by capturing the socket in a variable instead.
  2. Without a pinned `name:`, Compose derived the project name from the
     containing directory (`infrastructure`), so `infra-reset.sh`'s
     hardcoded `biolog_*` volume names never matched the actual
     `infrastructure_biolog_*` volumes Compose created - the reset script
     reported success while removing nothing. Fixed by pinning
     `name: biolog` in the compose file and renaming the volume keys so the
     final resolved names match what the scripts expect.

What was not exercised live: this specific machine already had unrelated
Docker containers from other projects running on `localhost:6379`, so the
Redis port collision (and the fix - overriding `REDIS_PORT` via
`infrastructure/.env`) was demonstrated but the *default* port was not
proven free of collisions on a clean machine. Port collisions are inherent
to any fixed-port local stack; `infrastructure/.env.example` exists
specifically so a colliding port can be remapped without touching the
compose file itself.
