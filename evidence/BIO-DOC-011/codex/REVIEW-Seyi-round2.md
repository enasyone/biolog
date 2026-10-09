# BIO-DOC-011 round-two review

Verdict: FAIL — CHANGES_REQUIRED

Reviewed remediation `a5989f2` and builder evidence `2fbe2f5` against BIO-DOC-011 AC01–AC06.

The original caller-forged mutation and unaudited-success findings are substantially closed: mutation routes require canonical provenance plus tenant-scoped manage/publish permissions, lineage mutations are serialized, and ambiguous audit writes are checked before persistence continues. One authorization gap remains.

## BIO-DOC-011-F01 — unpublished template reads have no RBAC gate (High)

`ListVersionsAsync`, `GetVersionAsync`, and `GetPublishedAsync` call `ActorProvenanceGate.Require`, but deliberately perform no `IPermissionGateway` lookup. Provenance proves only which actor asserted the request; it does not establish that the actor is an authorised BTS/tenant administrator.

Consequently, any provenance-valid actor in the resolved tenant can enumerate version history and read draft/test/approval template definitions, including reference-layout hashes, region names/geometry, corpus results, and author metadata. The user story limits the editor to authorised administrators, and these reads are part of that editor rather than BIO-DOC-010's separate public global-template surface.

Reproduction: grant an actor no `document_template_editor.*` permission, send a valid provenance proof, and call `GET /api/v1/document-template-drafts/{templateId}/versions` or a version route. The endpoint returns the content. Expected: a tenant-scoped read/manage permission check, with negative tests for unprivileged and cross-tenant actors.

## Verification

- Functional: PASS for lifecycle, corpus gate, and immutability by inspection and builder evidence.
- Security/RBAC: FAIL (F01).
- Tenant isolation: PASS for server-resolved tenant scoping; authorization within a tenant fails.
- Concurrency/audit integrity: PASS for the reported round-one races by inspection of the shared lineage lock and verified audit-before-persist path.
- UI/regression: PASS WITH TRACKED HYBRID LIMITATION; `/document-verify` remains unchanged and tenant-template registry/editor UI integration is explicitly deferred.

