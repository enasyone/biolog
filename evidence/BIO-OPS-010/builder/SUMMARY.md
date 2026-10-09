# BIO-OPS-010 local publication preparation

Date: 2026-10-08

Audited branch: `develop`

Audited commit: `a9864e9f24f2de9ea8aa7881718d1353cb9f6aef`

## Verdict

**BLOCKED — AC03 privacy remediation and AC04 human approval remain required.** No remote was added, nothing was pushed or tagged, and no release-state approval field was changed.

## AC01 — local acceptance package

- Proposed immutable publication range: `fe64f510037d2522fceabaafd42c4702b8680d1f^..a9864e9f24f2de9ea8aa7881718d1353cb9f6aef` (the root through the audited `develop` head; 1,443 commits). Because the first commit has no parent, tools that do not accept the caret form should use `git rev-list --reverse a9864e9` and include the root explicitly.
- Proposed tag, not created: `biolog-local-acceptance-2026-10-08`, targeting `a9864e9f24f2de9ea8aa7881718d1353cb9f6aef` after remediation and approval.
- Governance has 111 `DONE` stories. All 111 have acceptance `PASS`; every current recorded review verdict is `PASS`.
- 100 stories have resolvable builder pointers and all 100 commits are ancestors of audited `develop`.
- Ten early/current records have no builder pointer: `BIO-FND-004`–`006`, `BIO-ARC-001`–`005`, `BIO-OPS-008`, and `BIO-OPS-009`. Their acceptance/review records are PASS; the two OPS story merge tips are ancestors of audited head.
- `BIO-UX-009` has a malformed/stale builder pointer, `382f644d`, which does not resolve. The accepted branch tip is `63951d624de36964c8bd048383d0b2a2bc635616`, merged by `b1aafdcbc6c808477c75520f6621fcf57b839039`; both accepted content and merge are ancestors of audited head. Correct the governance pointer before publication.
- Pre-existing working-tree changes mean the checkout itself is not clean: modified `web/next-env.d.ts`; untracked `evidence/BIO-FND-001/acceptance/`, `evidence/BIO-FND-001/codex/`, and `test-results/`. None is part of the audited commit.

Checks on the audited code state:

- `.NET` build: PASS, 0 errors; two NU1900 warnings because the sandbox could not reach the NuGet vulnerability index.
- Web TypeScript: PASS (`pnpm --dir web exec tsc --noEmit`).
- Frontend unit suite: PASS, 68 files / 550 tests.
- Module project-reference check: PASS.
- Traceability check: PASS, 37 implemented stories.
- Contract-freeze harness: PASS, 39 artifacts / 83 future stories.

Two discarded operator-command errors are not product failures: a nonexistent frontend `test` script was initially invoked before using `test:unit`, and a nonexistent contract-freeze path was tried before running `tests/foundation/BIO-ARC-006.test.sh`.

## AC02 — no remote

`git remote -v` returned no entries. No remote or tag was created.

## AC03 — secret and privacy scan

- `gitleaks detect --source . --config .gitleaks.toml --no-banner --redact`: PASS; 1,314 commits and about 134.69 MB scanned, no secret leak reported.
- Privacy review: FAIL pending remediation. Git history contains author identity `Emeka <enasyfoto@gmail.com>` and tracked evidence contains the workstation's absolute home-directory path in 174 current files. A public history rewrite or explicit informed publication decision is required.
- Synthetic-data-shaped values found and retained for review: `KELECHI.N@EXAMPLE.EDU.NG`; `+2348012345678`; `+234 801 234 5678`; `1990-01-01`; `12345678901`; `STAFF-00042`; and `000-00-0000`. They occur in tests/contracts and appear deliberately synthetic, but should be labelled consistently so they cannot be mistaken for real personal data.
- `+44` matches in evidence logs are Flutter progress counters (for example `00:03 +44:`), not telephone numbers.
- Filename/source-name matches containing “Credential” are application classes, not private-key artifacts.
- All inspected commits are unsigned. This is not a secret leak, but future signed-commit policy must be prospective rather than retroactively asserted.

## Required decisions before publication

1. Decide whether to rewrite author metadata and absolute workstation paths from all published history, or explicitly approve those disclosures.
2. Correct the stale `BIO-UX-009` builder pointer.
3. Re-run both history secret scanning and privacy searches against the rewritten/final commit.
4. Obtain AC04 human approval before enabling GitHub, adding a remote, tagging, or pushing.

The post-publication AC06 draft is in `AC06-PUBLICATION-CONTROLS-DRAFT.md`.

## Path-remediation rerun

Human direction retained the author email and required local-path removal. Commit `e33e114` mechanically replaced the literal workstation workspace prefix with `<workspace>/` in all 174 affected tracked evidence files (5,782 occurrences). Current-tree privacy scan now finds zero occurrences of that workstation home-directory path, and both current-tree and Git-history gitleaks scans remain clean.

AC03 is **not yet clean for the proposed full-history publication**: 105 earlier commits still contain the literal path. A normal follow-up commit cannot remove content from historical objects. Publication therefore requires either an approved history rewrite that substitutes the path in every affected commit, or a new squashed/root publication commit that excludes the old objects. After that operation, rerun gitleaks and `git grep` across every reachable publication commit before approving AC03.
