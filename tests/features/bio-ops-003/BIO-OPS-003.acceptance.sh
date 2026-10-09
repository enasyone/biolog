#!/usr/bin/env bash
set -u

failures=0
pass() { printf 'PASS: %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1"; failures=$((failures + 1)); }

production='api/features/bio-crd-007/ProductionService.cs'
production_gateway='api/features/bio-crd-007/Domain/ICardIssueGateway.cs'
lifecycle='api/features/bio-crd-006/CardLifecycleService.cs'
lifecycle_gateway='api/features/bio-crd-006/Domain/ICardIssueGateway.cs'
lifecycle_module='api/Biolog.Api/Modules/BioCrd006Module.cs'

# CRD-007 correctly blocks direct production activation until the latest job passed.
if rg -q '!latest.Status.AllowsActivation\(\)' "$production" \
  && rg -q 'cardIssueGateway.ActivateAsync' "$production"; then
  pass 'AC04 direct production activation is gated on a passed job'
else
  fail 'AC04 direct production activation is not gated on a passed job'
fi

# CRD-006 correctly owns permanent revocation + replacement activation.
if rg -q 'BeginRevocationAsync' "$lifecycle" \
  && rg -q 'ConfirmRevocationAsync' "$lifecycle" \
  && rg -q 'cardIssueGateway.ActivateAsync' "$lifecycle"; then
  pass 'AC03 lifecycle replacement path permanently revokes earlier cards'
else
  fail 'AC03 replacement path does not permanently revoke earlier cards'
fi

# Integrated acceptance requires ONE path to enforce both halves. Neither narrow gateway
# exposes the other feature's state/capability, proving the composition is absent.
if rg -q 'productionStatusGateway.HasPassedAsync' "$lifecycle" \
  && rg -q 'productionService.ListJobsAsync' "$lifecycle_module"; then
  pass 'AC04 replacement activation consults production outcome'
else
  fail 'AC04 replacement activation can activate a failed/unproduced replacement'
fi

# SY-OPS003-02 (Kemi's round-2 ruling): a genuine shared, host-layer lock BOTH features acquire
# for the identical key - not either feature's own independent lock alone.
shared_lock='api/Biolog.Api/Cards/CardActivationBarrierLockRegistry.cs'
if [ -f "$shared_lock" ] \
  && rg -q 'activationBarrier.AcquireAsync' "$lifecycle" \
  && rg -q 'activationBarrier.AcquireAsync' "$production"; then
  pass 'AC04 production status remains valid atomically through replacement activation'
else
  fail 'AC04 replacement activation uses a stale production snapshot across distinct locks'
fi

if (( failures > 0 )); then
  printf '%d required integrated invariant(s) failed\n' "$failures"
  exit 1
fi

printf 'PASS: BIO-OPS-003 integrated acceptance invariants satisfied\n'
