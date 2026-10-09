#!/usr/bin/env bash
set -u

failures=0
pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1"; failures=$((failures + 1)); }

host='api/Biolog.Api/Biolog.Api.csproj'
live='web/features/documents/live.ts'
port='web/features/documents/ports.ts'
# The result-shape fields (confidence/provenance) live on EvidenceStepResult/EvidenceScenario in
# evidence.ts, not on the DocumentsPort interface itself (ports.ts declares the eight METHOD
# signatures only, returning EvidenceStepResult - it carries no field-level text at all). The two
# checks below originally pointed at `port`, which can never contain either field's text; fixed
# to point at the file that actually declares the contract, same stringency intended (required,
# not merely present-in-some-form).
evidence='web/features/documents/evidence.ts'
comparison='api/features/bio-doc-008/Biolog.Features.EvidenceComparison/Model/CrossComparisonResult.cs'
authenticity='api/features/bio-doc-009/Biolog.Features.AuthenticityAssessment/Model/AuthenticityPolicyEngine.cs'
foundation='api/features/bio-doc-001/Biolog.Features.DocumentVerify/Model/PersonFieldChangeProposal.cs'

# These checks prove the component capabilities required by the ACs. They deliberately do not
# accept component-only evidence as proof that the shipped Document Verify operation composes them.
if rg -q 'FieldsRequiringReview' "$comparison" && rg -q 'FieldMatchStatus.Mismatch' "$comparison"; then
  pass 'AC02 comparison component keeps mismatched fields visible'
else
  fail 'AC02 comparison component loses mismatched fields'
fi

if rg -q 'AuthenticityCheckOutcome.NotPerformed' "$authenticity" \
  && rg -q 'if \(!nfc.Available\)' "$authenticity"; then
  pass 'AC03 policy component distinguishes unsupported NFC from pass/fail'
else
  fail 'AC03 policy component collapses unsupported NFC into pass/fail'
fi

if rg -q 'returns a NEW record' "$foundation" && rg -q 'PERSON record' "$foundation"; then
  pass 'AC04/AC05 foundation exposes non-destructive PERSON change proposals'
else
  fail 'AC04/AC05 foundation lacks non-destructive PERSON change proposals'
fi

# Acceptance is for one explainable evidence operation, not disconnected class libraries. Every
# named engine must be callable by the shipped host and its result must reach the existing UI port.
for engine in 002 003 004 007; do
  if rg -q "bio-doc-$engine/" "$host"; then
    pass "AC01 BIO-DOC-$engine engine is linked into the shipped API host"
  else
    fail "AC01 BIO-DOC-$engine engine is not linked into the shipped API host"
  fi
done

for endpoint in mrz barcode ocr nfc; do
  if rg -q "MapPost\(.*documents/$endpoint" api/Biolog.Api api/features -g '*.cs'; then
    pass "AC01 /api/v1/documents/$endpoint is implemented"
  else
    fail "AC01 /api/v1/documents/$endpoint has no implemented endpoint"
  fi
done

if rg -q 'provenance' "$evidence" && rg -q 'provenance' "$live"; then
  pass 'AC01 shipped UI port returns engine provenance'
else
  fail 'AC01 shipped UI port has no provenance field'
fi

if rg -q 'confidence: number' "$evidence" && ! rg -q 'confidence\?: number' "$evidence"; then
  pass 'AC01 every engine result contract carries confidence'
else
  fail 'AC01 shared engine result contract does not require confidence'
fi

if (( failures > 0 )); then
  printf '%d required integrated invariant(s) failed\n' "$failures"
  exit 1
fi

printf 'PASS: BIO-OPS-005 integrated acceptance invariants satisfied\n'
