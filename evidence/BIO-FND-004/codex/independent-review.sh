#!/usr/bin/env bash
set -u

repo="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d "${TMPDIR:-/tmp}/bio-fnd-004-codex.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT
fail=0

pass() { printf 'PASS %s\n' "$1"; }
fail_case() { printf 'FAIL %s\n' "$1"; fail=1; }

git clone -q --no-local "$repo" "$tmp/repo"
client="$tmp/repo"
story="BIO-QA-900"
head_hash="$(git -C "$client" rev-parse HEAD)"
other_hash="$(git -C "$client" rev-parse HEAD~1)"

write_metadata() {
  local stage="$1" result="$2" hash="$3"
  mkdir -p "$client/evidence/$story/$stage"
  printf '{\n  "story_key": "%s",\n  "commit_hash": "%s",\n  "test_command": "synthetic-check --stage %s",\n  "result": "%s",\n  "timestamp": "2026-09-20T15:00:00Z",\n  "reviewer": "Seyi",\n  "data_classification": "SYNTHETIC"\n}\n' \
    "$story" "$hash" "$stage" "$result" >"$client/evidence/$story/$stage/metadata.json"
}

run_check() {
  (cd "$client" && python3 scripts/check-evidence.py "$story" "$@") >"$tmp/last.log" 2>&1
}

for stage in builder adversarial security acceptance; do
  write_metadata "$stage" PASS "$head_hash"
done
if run_check; then pass 'AC01-04 complete valid evidence passes'; else fail_case 'AC01-04 complete valid evidence passes'; fi

rm -rf "$client/evidence/$story/adversarial" "$client/evidence/$story/security" "$client/evidence/$story/acceptance"
if run_check; then fail_case 'AC03 builder-only evidence fails when all stages are required'; else pass 'AC03 builder-only evidence fails when all stages are required'; fi
if run_check --required-stages builder; then pass 'AC03 builder-only evidence may pass only when explicitly scoped to builder'; else fail_case 'AC03 builder-only evidence may pass only when explicitly scoped to builder'; fi
for stage in adversarial security acceptance; do write_metadata "$stage" PASS "$head_hash"; done

printf '{ malformed\n' >"$client/evidence/$story/security/metadata.json"
if run_check; then fail_case 'AC02/03 malformed metadata fails closed'; else pass 'AC02/03 malformed metadata fails closed'; fi
write_metadata security PASS "$head_hash"

printf '{"story_key":"%s"}\n' "$story" >"$client/evidence/$story/security/metadata.json"
if run_check; then fail_case 'AC02/03 partial metadata fails closed'; else pass 'AC02/03 partial metadata fails closed'; fi
write_metadata security PASS "$head_hash"

write_metadata security PASS 0000000000000000000000000000000000000000
if run_check; then fail_case 'AC02/03 nonexistent commit fails'; else pass 'AC02/03 nonexistent commit fails'; fi
write_metadata security PASS "$head_hash"

write_metadata security PASS "$other_hash"
if run_check; then fail_case 'AC02/03 wrong but locally existing commit fails'; else pass 'AC02/03 wrong but locally existing commit fails'; fi
write_metadata security PASS "$head_hash"

for stage in builder adversarial security acceptance; do write_metadata "$stage" FAIL "$head_hash"; done
if run_check; then fail_case 'AC03 failed stage results prevent clean acceptance'; else pass 'AC03 failed stage results prevent clean acceptance'; fi

write_metadata builder PASS "$head_hash"
write_metadata adversarial PASS "$head_hash"
write_metadata security PASS "$head_hash"
write_metadata acceptance PASS "$head_hash"
sed -i.bak 's/"SYNTHETIC"/"PRODUCTION"/' "$client/evidence/$story/adversarial/metadata.json"
rm -f "$client/evidence/$story/adversarial/metadata.json.bak"
if run_check; then fail_case 'AC04 production-data classification is rejected'; else pass 'AC04 production-data classification is rejected'; fi

exit "$fail"
