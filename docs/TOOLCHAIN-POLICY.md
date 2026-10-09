# BIOLOG local toolchain policy (BIO-FND-002)

Every Munder agent and every developer laptop must build BIOLOG with the same
toolchain. `toolchain/versions.json` is the single source of truth; the files
below are native pins kept in sync with it so each tool enforces its own pin
where the tool supports that.

## Pinned toolchain (recorded at first bootstrap, 2026-09-20)

| Category (AC01) | Tool | Pinned version | Match rule | Native pin file |
|---|---|---|---|---|
| .NET LTS | .NET SDK | `10.0.201` | exact | `global.json` (`rollForward: disable`) |
| Node LTS | Node.js | `20.20.2` | exact | `.nvmrc` |
| Package manager | pnpm | `9.12.0` | exact | `package.json` (`packageManager`, Corepack-managed) |
| Flutter | Flutter SDK | `3.44.8` (channel `stable`, Dart `3.12.2`) | exact | none (see below) |
| Docker | Docker Engine / Compose | `29.3.1` / `5.1.1` | minimum major.minor | none (host-managed) |
| Database client | MySQL client | `8.4.0` family | minimum major.minor | none (host-managed) |
| Secret scanner | gitleaks | `8.30.1` | exact | none (see below) |

Gitleaks was added as a seventh category via **TD-FND002-PIN-GITLEAKS**
(2026-10-08), picking up a gap this document originally deferred (see "Known
gaps" below) once it stopped being optional: Kemi's ruling on BIO-OPS-006
round 3 found the no-gitleaks pattern-scan fallback in
`scripts/secret-scan.sh`/`scripts/history-secret-scan.sh` had failed three
review rounds in a row on three different defect classes, and BIO-OPS-006
round 4 removed that fallback entirely — both scripts now require gitleaks
and fail closed when it is absent, so a machine without it pinned and
installed cannot pass a secret-scan gate at all, not just run a weaker one.
The pinned version was corrected the same day, `8.24.3` → `8.30.1`: the
initial value was recorded without network access to confirm it against the
live release feed; once gitleaks was actually installed (via brew, to
unblock BIO-OPS-006 verification) `gitleaks version` reported `8.30.1`, and
the manifest was corrected to match in its own follow-on commit per the
upgrade policy below.

Run `scripts/check-toolchain.sh` to compare what is actually installed against
this table. It reads `toolchain/versions.json`, never this document, so this
table must stay in sync with that file — treat a mismatch between them as a
bug.

### Match rules

- **exact** — the installed version must equal the pinned version exactly.
  Used for everything a project-local pin file fully controls (.NET SDK via
  `global.json`, Node via `.nvmrc`/`engines`, pnpm via Corepack, Flutter via
  this policy). This is what makes "every Munder agent builds the same
  application" (the story's user need) actually true bit-for-bit. Gitleaks is
  also pinned exact, for a different reason than the project-local-pin-file
  group above: it has no per-project pin file at all, but its detection
  rules (not just its CLI interface) can change release to release, and the
  whole point of mandating it (BIO-OPS-006 round 4) is that every Munder
  agent's secret-scan gate reaches the identical pass/fail decision.
- **minimum major.minor** — the installed version's major.minor must be
  greater than or equal to the pinned baseline. Used only for host-managed
  software we cannot pin per-project (Docker Desktop/Engine, the OS-installed
  MySQL client): patch releases are applied outside repository control, so
  requiring an exact patch would make the policy impossible to satisfy in
  practice. A major or minor **downgrade** below the baseline is still
  unsupported and fails the check.

  This rule alone does not forbid a numerically *larger* major (e.g. MySQL
  9.x >= 8.4.0 passes a plain "greater or equal" comparison). Docker
  intentionally allows that — a newer Docker major is expected to keep
  working. The MySQL client does not: its supported family is pinned to
  MySQL 8.4 specifically (it must match the containers in `infrastructure/`,
  BIO-FND-003), so `scripts/check-toolchain.sh` additionally rejects any
  major version other than the pinned one for that category only
  (`check_min_same_major`, not the plain `check_min` Docker uses).

### Native pin cross-check (AC02)

`scripts/check-toolchain.sh` does not just read installed binaries — it also
reads `.nvmrc`, `global.json`, and `package.json` back and compares each
declared value against `toolchain/versions.json`. A native pin file that has
drifted from the manifest (edited directly, merged incorrectly, or simply
missing) fails the check even if every installed binary on the machine
happens to match the manifest. This is what makes the manifest an actually
enforced single source of truth rather than a value that only has to agree
with itself.

### Known gaps at first bootstrap

- **pnpm** and the **MySQL client** are not installed on the reference
  bootstrap machine. Their pinned versions above are policy targets, not
  confirmed-installed versions (`toolchain/versions.json` marks each with
  `"installed_at_bootstrap": false`). `scripts/check-toolchain.sh` correctly
  reports both as `NOT INSTALLED` until they are installed — that is expected
  today, not a defect (see BIO-FND-002 builder evidence).
- **gitleaks** was not installed at first bootstrap either, but was then
  installed (via brew) to unblock BIO-OPS-006 verification, and its pinned
  version was confirmed directly against that installed binary — see the
  `8.24.3` → `8.30.1` correction noted above. `installed_at_bootstrap` for
  this category reflects that it is now confirmed-installed on the reference
  machine, unlike pnpm/MySQL above. Being absent is not merely a
  degraded-but-usable state for this one: `scripts/secret-scan.sh`/
  `scripts/history-secret-scan.sh` fail closed without it (BIO-OPS-006 round
  4), so install it before relying on either gate.
- The pnpm version was chosen without live npm-registry access; corepack will
  fetch it once installed but the exact patch has not been confirmed against
  the registry. Confirm it with `corepack use pnpm@<version>` at first real
  install, then update `package.json` and `toolchain/versions.json` together
  in the same commit if it needs correcting.
- Flutter has no widely-supported native per-repo pin file until a real
  Flutter project exists under `mobile/` (Flutter's own project-level
  constraint lives in a package's `pubspec.yaml`, not at the workspace root).
  Until `mobile/` is scaffolded, `toolchain/versions.json` is Flutter's only
  pin; `scripts/check-toolchain.sh` enforces it directly against the
  installed `flutter` binary.
- **`dotnet build`/`dotnet restore` hangs at "Determining projects to restore..." for
  exactly `00:05:01`-`00:05:02` with zero diagnostic output, inside a sandboxed agent
  session — this is a sandbox network restriction on `dotnet restore`, not an SDK defect.**
  Corrected here after an earlier, wrong diagnosis (see below): **run the affected `dotnet`
  command with the sandbox disabled** (Claude Code's `dangerouslyDisableSandbox`, or your
  agent runtime's equivalent bypass) — this consistently fixes it in seconds (confirmed:
  `dotnet build api/Biolog.Api/Biolog.Api.csproj` went from a guaranteed 5:01+ hang, sandboxed,
  to a clean 7.78s build, unsandboxed, same commit, same machine). Matches a pattern already
  seen independently elsewhere this session (Kemi's ADR-0006 verification also needed the
  sandbox disabled for `dotnet` operations specifically, while pure bash/file-check harnesses
  ran fine sandboxed) — this is general advice for any `dotnet` invocation in a sandboxed
  session on this machine, not specific to one story or worktree.
  **Retracted: this was previously (wrongly) diagnosed as an incomplete `10.0.201` SDK
  install** — `/usr/local/share/dotnet/sdk/10.0.201/Sdks/` genuinely is missing
  `Microsoft.NET.SDK.WorkloadAutoImportPropsLocator`/`Microsoft.NET.Sdk.WorkloadManifestTargetsLocator`,
  and `/usr/local/share/dotnet/sdk-manifests/` genuinely is still stuck at `10.0.100` (both
  real, still-true observations about this machine's install) — but neither is the cause of
  the hang. The human ran the reinstall this diagnosis called for (`sudo installer -pkg
  <cached-10.0.201-pkg> -target /`); it did not fix anything (`sdk-manifests/10.0.201` still
  does not exist afterward), and a sandboxed build still hung identically post-reinstall,
  while an unsandboxed build of the exact same commit succeeded immediately — proof the
  install was never the variable that mattered. The `MSBuildEnableWorkloadResolver=false`
  workaround this entry previously recommended was tested and does **not** help either (it
  addresses a workload-resolver problem, and there wasn't one). **Lesson, worth generalizing
  past this one gap**: a theory that explains an observed symptom is not confirmed by the
  symptom matching — it has to survive a controlled comparison (here: same commit, sandbox
  on vs. off) before being written down as a root cause, not just before being reported as
  one. This is the same class of miss as an earlier wrong "sandbox DNS limitation" theory on
  a different story this session (BIO-DEV-001's NU1900 investigation) — a plausible story
  that fit the symptom, escalated before it was tested side-by-side against the one variable
  that actually mattered. If you hit exactly this signature — a ~301-second hang, no error
  text at all — try disabling the sandbox for that one command first, before suspecting a
  circular project reference or a stale NuGet cache (both are real, distinct failure modes
  this codebase has also hit, with their own very different signatures: a genuine circular
  `ProjectReference` also hangs at "Determining projects to restore..." but is deterministic
  per affected project graph regardless of sandbox state, and is caught structurally today by
  `scripts/check-module-project-references.sh`; a stale/corrupt `obj/project.nuget.cache`
  produces a fast `NU1900` failure, not a hang, sandboxed or not).

## Upgrade policy (AC03)

**No feature story may change a pinned toolchain version.** Changing any row
in `toolchain/versions.json` (or a native pin file) requires its own
dedicated toolchain story reviewed the same way as this one — never a silent
bump inside unrelated feature work. `scripts/check-toolchain.sh` enforces
this mechanically: any installed version that does not satisfy its pinned
row's match rule is reported as drift and fails the check, whether the drift
is an accidental downgrade or an unapproved upgrade.

## Lock file policy (AC03)

Every ecosystem workspace must commit its lock file once it exists, and
`scripts/check-toolchain.sh` treats a manifest without a committed matching
lock file as a policy violation:

| Workspace | Manifest | Required lock file |
|---|---|---|
| `web/` | `package.json` | `pnpm-lock.yaml` |
| `api/` | any `*.csproj`/`*.sln` | a committed NuGet `packages.lock.json` per project |
| `mobile/` | `pubspec.yaml` | `pubspec.lock` |

None of these workspaces exist yet (`web/`, `api/`, `mobile/` are still
placeholders owned by later stories — see their `README.md`), so this table
is enforced but currently a no-op. It exists now so those later stories
inherit the check for free instead of needing to invent it.

## Version check command (AC04)

```sh
scripts/check-toolchain.sh
```

Exits `0` only when every pinned category is satisfied and every existing
manifest has a committed lock file; otherwise it prints a `PASS`/`FAIL` line
per category plus the lock-file check and exits `1`. See
`tests/foundation/BIO-FND-002.test.sh` for developer tests mapped to each AC,
including synthetic drift and missing-lock-file regression cases, and
`evidence/BIO-FND-002/builder/` for a recorded run against this machine.
