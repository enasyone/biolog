# infrastructure/

Local Docker development infrastructure (BIO-FND-003): MySQL control-plane
and tenant databases, Redis, RabbitMQ, Azurite (Blob Storage emulator), and
Mailpit. See `docs/LOCAL-INFRASTRUCTURE.md` for the full guide and
`scripts/infra-up.sh` / `infra-down.sh` / `infra-reset.sh` /
`wait-for-infra.sh` / `validate-infra-compose.sh` for the commands used to
bring it up, tear it down, reset its data, wait for readiness, and validate
it without a running Docker daemon.

`docker-compose.local.yml` is this repository's own file, independent of
the control pack's `06-LOCAL-DEVELOPMENT/docker-compose.local.yml` template
it was adapted from. `.env` (gitignored - copy from `.env.example`) is
where local port/credential overrides go; every value has a safe synthetic
default so the stack starts with zero required configuration.

Azure-readiness abstractions (production adapters swapping Redis/RabbitMQ
for Azure-native services, per
`02-ARCHITECTURE/MASTER-TECHNICAL-SPECIFICATION.md` section 1) are
introduced by the API/service-shell stories that consume this
infrastructure, not by this directory itself.
