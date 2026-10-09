from pathlib import Path

service = Path("api/features/bio-crd-006/CardLifecycleService.cs").read_text()
method = service.split("public async Task<CardIssueSnapshot> ActivateReplacementAsync(", 1)[1]
method = method.split("private async Task<CardLifecycleRecord?> BeginRevocationAsync", 1)[0]

read = method.index("cardIssueGateway.GetAsync")
begin = method.index("await BeginRevocationAsync")
activate_cas = method.index("cardIssueGateway.ActivateAsync")
compensate = method.index("await CompensateRevocationAsync")
confirm = method.index("await ConfirmRevocationAsync")
durable_prior_state = "PriorLockStateBeforeRevocationPending" in service
pending_denied = "CardLockState.RevocationPending => (PresentationOutcome.Denied" in service

print(f"gateway_read_offset={read}")
print(f"begin_revocation_offset={begin}")
print(f"authoritative_activation_offset={activate_cas}")
print(f"compensation_offset={compensate}")
print(f"confirmation_offset={confirm}")
print(f"durable_prior_state={durable_prior_state}")
print(f"pending_state_denies_presentation={pending_denied}")
print(f"two_phase_order={read < begin < activate_cas < compensate < confirm}")

if not (read < begin < activate_cas < compensate < confirm):
    raise SystemExit(
        "FAIL: activation is not enclosed by the expected begin/compensate/confirm revocation flow"
    )
if not durable_prior_state or not pending_denied:
    raise SystemExit("FAIL: pending revocations are not durably recoverable and conservatively denied")

print("PASS")
