# Story traceability and local acceptance

Story specifications live under `governance/stories/` as JSON. Each specification exposes
stable requirement IDs, acceptance criterion (AC) IDs, test declarations and evidence
declarations. Tests declare the AC IDs they cover in the story specification; evidence uses
the same IDs, producing an executable requirement → AC → test → evidence graph.

Validate a story with:

```sh
python3 scripts/check-traceability.py governance/stories/BIO-FND-010.json
```

The command reports every malformed link, unknown ID, missing/empty test or evidence file,
uncovered requirement and AC without both test and evidence coverage. Any gap returns a
non-zero status and prints `ACCEPTANCE: BLOCKED`.

Evidence declarations point to BIO-FND-004 `metadata.json` files and state their stage.
The checker requires the correct story, locally resolvable reviewed commit, `PASS` result,
reproducible command and named reviewer. Acceptance additionally requires every stage named
by `required_acceptance_stages`; builder-only evidence cannot authorize a decision.
The checker itself enforces the non-removable `builder` plus `adversarial` minimum. A story
may add stages such as security, but cannot weaken or delete independent review in its own
mutable specification.
Each metadata file also carries its own `stage`. One resolved file path may satisfy only one
declaration, the internal stage must match, and builder/adversarial reviewers must be
distinct. Relabelling or duplicating builder evidence therefore cannot impersonate review.

Validate every implemented foundation story, deriving the inventory from its executable
test harness, with:

```sh
python3 scripts/check-traceability.py --all
```

The quality gate runs this inventory mode, so adding a story harness without its story spec
is a blocking gap rather than an invisible omission.

An independent acceptor may create a local decision record only through the same check:

```sh
python3 scripts/check-traceability.py governance/stories/BIO-FND-010.json \
  --record-acceptance evidence/BIO-FND-010/acceptance/traceability-decision.json \
  --actor Chinedu
```

The owner cannot self-accept. Existing records are never overwritten, paths cannot escape
the repository, and no decision file is created unless the graph is gap-free. This tool is
local governance; it does not replace independent review or the orchestration state machine.
