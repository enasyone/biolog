from pathlib import Path

source = Path("api/features/bio-crd-006/CardLifecycleService.cs").read_text()
method = source.split("public async Task<CardIssueSnapshot> ActivateReplacementAsync(", 1)[1]
method = method.split("private async Task<CardLifecycleRecord?> BeginRevocationAsync", 1)[0]

activate = method.index("cardIssueGateway.ActivateAsync")
begin = method.index("await BeginRevocationAsync")
confirm = method.index("await ConfirmRevocationAsync")

print(f"activation_offset={activate}")
print(f"begin_revocation_offset={begin}")
print(f"confirm_revocation_offset={confirm}")
print(f"two_phase_revocation_surrounds_activation={begin < activate < confirm}")

if not (begin < activate < confirm):
    raise SystemExit(
        "FAIL: earlier cards are not denied before activation and permanently confirmed after it"
    )

print("PASS")
