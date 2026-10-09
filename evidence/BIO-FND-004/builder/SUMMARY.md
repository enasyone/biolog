# BIO-FND-004 — Builder evidence

Implementation commit: `dd255acaf4bf0195db236f8b980b3050849d532f`

## CHANGES_REQUIRED round 1

Seyi identified two high-severity false-success paths. Fix commit `dd255ac`
binds every required stage's `commit_hash` to the explicit commit under review
(`HEAD` by default, or `--commit`) and treats every result other than `PASS` as
a blocking gap. The builder regression suite and Seyi's unchanged independent
review script now reject both stale locally-existing commits and complete
four-stage evidence whose results are `FAIL`.

## Acceptance-criterion traceability

| Acceptance criterion | Implementation | Developer test | Evidence |
|---|---|---|---|
| BIO-FND-004-AC01 | `evidence/README.md`; `evidence/_template/{builder,adversarial,security,acceptance}/` | `ac01_template_stages` | `developer-test-output.txt` |
| BIO-FND-004-AC02 | `evidence/_template/metadata.json`; validation in `scripts/check-evidence.py` | `ac02_metadata_contract` | `metadata.json`; `developer-test-output.txt` |
| BIO-FND-004-AC03 | `scripts/check-evidence.py` reports gaps and exits non-zero | `ac03_missing_story_fails`, `ac03_missing_stage_fails`, `ac03_missing_metadata_fails`, `ac02_ac03_rejects_stale_commit_and_failed_results` | `developer-test-output.txt` |
| BIO-FND-004-AC04 | `data_classification` is mandatory and restricted to `SYNTHETIC` or `NO_TEST_DATA` | `ac04_synthetic_marked`, `ac04_checker_enforces_classification` | `metadata.json`; `developer-test-output.txt` |

## Additional checks

- `scripts/validate-bootstrap.sh`: PASS.
- `scripts/secret-scan.sh --all`: PASS using the built-in fallback scanner.
- `python3 -m py_compile scripts/check-evidence.py`: PASS.
- `git diff --check`: PASS.
- `git remote -v`: empty; no remote or push was added.

The older `tests/foundation/BIO-FND-001.test.sh` returned 10/11 only in this
linked feature worktree. Its QA-01 fixture assumes a clone has a local `main`
branch (`git branch -f develop main`), but a clone whose source HEAD is this
feature branch has only that local branch, so fixture setup stops before adding
the synthetic secret. The underlying scanner and bootstrap validator both pass,
and the scanner independently rejected the same whitespace-path fixture when
run without that invalid branch assumption. BIO-FND-004 does not modify the
BIO-FND-001 test because that belongs to another completed story.
