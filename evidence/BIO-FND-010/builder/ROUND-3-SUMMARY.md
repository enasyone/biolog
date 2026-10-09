# BIO-FND-010 — QA-03 remediation

- **Remediation commit:** `3ae102a30bcf75460771b9695ef2ba6fdbfed8e6`
- **Original finding:** `evidence/BIO-FND-010/codex/REREVIEW-ROUND-2.md`

The checker now owns the non-removable minimum acceptance policy: both builder and
adversarial evidence are mandatory. A story specification may add security or other stages,
but deleting or weakening `required_acceptance_stages` cannot authorize acceptance.

## Verification

- Seyi's independent probes — 4/4 PASS, including the exact builder-only policy attack
- BIO-FND-010 focused suite — 14/14 PASS
- Complete inventory — PASS, 13 implemented stories
- Selected traceability graph — PASS, four ACs
- Full quality gate — QUALITY: PASS
- Secret scan — clean

Reviewer-owned evidence was not modified.
