# BIO-OPS-005 — Document Verify evidence acceptance

This acceptance story verifies one shipped, explainable Document Verify operation across MRZ,
barcode, OCR, document identification, NFC, comparison, authenticity and manual review. Isolated
engine capabilities are necessary but are not evidence that the integrated operation exists.

Run the independent structural acceptance probe from the repository root:

```bash
bash tests/features/bio-ops-005/BIO-OPS-005.acceptance.sh
```

The probe checks both layers:

- Component safety: mismatches remain explicit, unavailable NFC remains not performed, and PERSON
  changes use non-destructive proposals.
- Shipped composition: the API host links every named engine, exposes the live endpoints used by
  the approved Document Verify route, and the UI result contract carries provenance and confidence.

Current result: **PASS**. See `evidence/BIO-OPS-005/builder/REMEDIATION.md` for the full
composition built in response to Seyi's SY-OPS005-01 finding and Kemi's ruling on its shape
(`evidence/BIO-OPS-005/builder/BLOCKED.md` is kept as the historical record of the original
finding).

## What was built

A new feature project, `api/features/bio-ops-005/Biolog.Features.DocumentVerifyCoordination`
(`Biolog.Api.Modules.BioOps005Module`), composes BIO-DOC-001's session/evidence model with
BIO-DOC-002 (MRZ), BIO-DOC-003 (barcode), BIO-DOC-004 (OCR) and BIO-DOC-008 (cross-source
comparison) directly, and reaches BIO-DOC-009 (authenticity) / BIO-DOC-010 (document
identification) through two new narrow gateways
(`IAuthenticityAssessmentGateway`/`IDocumentIdentificationGateway`), implemented as adapters in
`BioOps005Module` wrapping those stories' own already-registered services - never a
`ProjectReference` to either. `AuthenticityAssessmentService.ComputeFromEvidence` and
`DocumentTemplatePackService.IdentifyFromEvidence` are new overloads added ALONGSIDE each
service's existing demo-cycling method, not replacing it.

Eight live endpoints under `POST /api/v1/documents/*` - one per `DocumentsPort` method, the exact
route family `web/features/documents/live.ts` already targets. `capture`'s response sets an
`HttpOnly` `document_session_id` cookie; every later step resolves its session from that cookie -
`DocumentsPort`'s eight methods stay zero-arg, no route was redesigned.

See `docs/features/bio-ops-005/DOCUMENT-VERIFY-COORDINATOR.md` for the full design: the raster
boundary disclosure, the NFC-unavailability disclosure, the pre-person/PERSON-proposal design,
and the cross-source comparison field-aliasing between MRZ's and OCR's own independent field
naming conventions.
