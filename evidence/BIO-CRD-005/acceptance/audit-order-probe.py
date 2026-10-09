from pathlib import Path

source = Path("api/features/bio-crd-005/CardIssueService.cs").read_text()
method = source[source.index("public async Task<CardIssue> IssueAsync"):source.index("public Task<CardIssue?> GetAsync")]
sequence_at = method.index("repository.NextSequenceNumber")
audit_at = method.index("await AuditAsync")
commit_at = method.index("repository.Create(issue)")

if not sequence_at < audit_at < commit_at:
    print("FAIL: IssueAsync does not follow compute -> audit -> commit order")
    raise SystemExit(1)

if "repository.AppendAsync" in method:
    print("FAIL: IssueAsync retains the old combined sequence-allocation/commit call")
    raise SystemExit(1)

print("PASS: IssueAsync computes the sequence, audits, then commits")
