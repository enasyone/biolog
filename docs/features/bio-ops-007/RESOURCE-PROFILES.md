# Laptop resource profiles

These are local assurance settings, not production sizing guidance. Run one heavy ecosystem at a
time unless the profile explicitly permits overlap; retain at least 25% memory and 10 GB disk free.

| RAM | Parallelism | Recommended local workload | Guardrail |
|---|---:|---|---|
| 16 GB | 1 heavy runner | One .NET test project, one Flutter shard, or Next build; 100 reconnects / 25 concurrent replacements per controlled repetition | Stop on swap growth above 2 GB or sustained memory pressure; do not run infrastructure plus all SDK builds together. |
| 32 GB | 2 heavy runners | API and Flutter in parallel; 500 reconnects / 100 concurrent replacements; 15-minute soak | Keep database/queue containers below 8 GB combined and preserve 8 GB headroom. |
| 64 GB | 4 heavy runners | Full API, UI, Flutter and infrastructure overlap; 2,000 reconnects / 250 concurrent replacements; 60-minute soak | Cap workers at physical performance cores; preserve 16 GB headroom and record thermal throttling. |

For every profile, use bounded queues, deterministic synthetic identifiers, per-run timeouts, and
an explicit recovery phase. Increase scale only after duplicate count remains zero and all queued
work reaches a terminal acknowledged or surfaced-conflict state.
