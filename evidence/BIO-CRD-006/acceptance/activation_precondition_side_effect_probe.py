from pathlib import Path

source = Path("api/features/bio-crd-006/CardLifecycleService.cs").read_text()
method = source.split("public async Task<CardIssueSnapshot> ActivateReplacementAsync(", 1)[1]
method = method.split("private async Task<CardLifecycleRecord?> BeginRevocationAsync", 1)[0]

validate = method.index("RequireExpectedVersion(expectedVersion, target.Version)")
reserve = method.index("idempotencyStore.Claim")
revoke = method.index("await BeginRevocationAsync")

print(f"idempotency_reservation_offset={reserve}")
print(f"precondition_validation_offset={validate}")
print(f"revocation_offset={revoke}")
print(f"validation_and_reservation_precede_revocation={reserve < revoke and validate < revoke}")

if not (reserve < revoke and validate < revoke):
    raise SystemExit(
        "FAIL: stale/missing activation preconditions are rejected only after earlier cards "
        "have been permanently revoked"
    )

print("PASS")
