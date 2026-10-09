# Munder Office Operating Rules (BIO-FND-005)

Authoritative boundaries for the Munder BIOLOG agent office. Machine-readable roster + permissions: `governance/munder-agents.json`.

## Roster (AC01)
Twelve agents, each with a role prompt (in the control pack `05-MUNDER/PROMPTS/`), an engine/model, and explicit permissions in `munder-agents.json`: Chinedu, Kemi, Tunde, Ngozi, Amina, Ifeanyi, Busola, Emeka (builders/orchestrator, Claude Code); Seyi, Zainab, Femi, Chioma (assurance, Codex).

## Dispatch eligibility (AC02)
Chinedu assigns a story only when: (a) every dependency is DONE, and (b) its conflict group and owned write-scope are not held by another active story. Batches are never combined. This mirrors the deterministic dispatcher (`automation/biolog.py` eligibility + conflict locks).

## Shared-contract authority (AC03)
Only Kemi approves changes to shared contracts, common primitives, and migration sequencing. No builder may alter a shared contract/migration without Kemi's recorded approval.

## No self-acceptance (AC04)
A builder may never mark its own story accepted. Completion routes: builder → independent review (Seyi, then Zainab/Femi where required) → acceptor (Chioma) via `biolog.py accept`. The acceptor is always distinct from the builder.

## Local-first / no publication (AC05)
No agent may add a GitHub remote or run `git push` before the W15 gate while `github_push_enabled` is false. Enforced by the repo pre-push guard (`.githooks/pre-push`, from BIO-FND-001).

## Escalation
Architecture/interface conflicts → Kemi. Product/business decisions → human owner.
