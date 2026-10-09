# Local acceptance evidence

Evidence is versioned under `evidence/<STORY-KEY>/`. Every story uses these
canonical stage directories:

```text
evidence/<STORY-KEY>/
├── builder/
├── adversarial/
├── security/
└── acceptance/
```

`codex/` is a legacy alias for `adversarial/` only for evidence created before
BIO-FND-004. New evidence must use the canonical names above.

## Metadata contract

Each stage that makes an evidence claim must contain `metadata.json`. Start from
[`_template/metadata.json`](./_template/metadata.json). These fields are required:

- `story_key`: uppercase story key, for example `BIO-FND-004`.
- `commit_hash`: the full 40-character Git commit tested. It must resolve in the
  local repository.
- `test_command`: exact reproducible command; it cannot be blank.
- `result`: `PASS` or `FAIL`.
- `timestamp`: UTC RFC 3339 timestamp ending in `Z`.
- `reviewer`: named human or agent responsible for the evidence.
- `data_classification`: must be `SYNTHETIC` for test fixtures/data, or
  `NO_TEST_DATA` when the command uses no test data.

Supporting logs, reports and screenshots belong beside the metadata. They must
not contain production personally identifiable information, secrets, biometrics
or tenant data.

## Traceability check

Validate all four completed stages for a story:

```sh
python3 scripts/check-evidence.py BIO-FND-004
```

By default, metadata must name the current `HEAD` commit. If evidence is stored
in a later evidence-only commit, name the exact builder commit under review:

```sh
python3 scripts/check-evidence.py BIO-FND-004 --commit <40-character-builder-commit>
```

During an earlier workflow stage, validate only evidence that is due:

```sh
python3 scripts/check-evidence.py BIO-FND-004 --required-stages builder
```

The checker prints every missing directory, metadata file, field, invalid value,
non-passing result, unresolved commit or mismatch with the commit under review,
and exits non-zero when any gap exists. Local acceptance must require all four
stages and a clean exit.

The committed `_template/` tree defines the directory convention. Copy it for a
new story; do not record a `PASS` until the stated command has actually run.
