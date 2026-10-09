from pathlib import Path

result = Path("api/features/bio-doc-008/Biolog.Features.EvidenceComparison/Model/CrossComparisonResult.cs").read_text()
feature_files = "\n".join(
    p.read_text() for p in Path("api/features/bio-doc-008/Biolog.Features.EvidenceComparison").rglob("*.cs")
)
compute = result.split("public static CrossComparisonResult Compute(", 1)[1]

has_audit_sink = "AuditSink" in feature_files or "AuditEvent" in feature_files
writes_audit = "WriteAsync" in compute or "AppendAsync" in compute
validates_version = "versionNumber <= 0" in compute or "versionNumber < 1" in compute
validates_actor = "IsNullOrWhiteSpace(computedByActorId)" in compute
validates_time = "TryParse" in compute and "computedAt" in compute
controls_version = any(
    token in feature_files
    for token in ("IVersionStore", "IComparisonRepository", "GetNextVersionAsync", "AllocateVersion")
)
allocate_offset = compute.index("versionStore.AllocateVersion")
audit_offset = compute.index("WriteAuditEventOrThrow")
has_release_or_transaction = any(
    token in feature_files for token in ("ReleaseVersion", "RollbackVersion", "CommitVersion", "IComparisonTransaction")
)

print(f"has_audit_sink={has_audit_sink}")
print(f"compute_writes_audit={writes_audit}")
print(f"validates_version={validates_version}")
print(f"validates_actor={validates_actor}")
print(f"validates_timestamp={validates_time}")
print(f"version_control_present={controls_version}")
print(f"version_allocated_before_audit={allocate_offset < audit_offset}")
print(f"release_or_transaction_present={has_release_or_transaction}")

if not has_audit_sink or not writes_audit:
    raise SystemExit(
        "FAIL: AC05 audit is represented only by caller-supplied attribution fields; "
        "no durable audit event is written"
    )
if not (validates_version and validates_actor and validates_time):
    raise SystemExit("FAIL: caller-supplied version/audit attribution is accepted without validation")
if not controls_version:
    raise SystemExit(
        "FAIL: version remains an arbitrary caller-supplied positive integer; no repository, "
        "previous-version check, or allocation step controls comparison version sequencing"
    )
if allocate_offset < audit_offset and not has_release_or_transaction:
    raise SystemExit(
        "FAIL: version is committed before the mandatory audit write and is never released; "
        "an audit failure burns the legitimate version and makes a same-version retry stale"
    )

print("PASS")
