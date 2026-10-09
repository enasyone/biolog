# BIO-OPS-005 — BLOCKED

## Finding SY-OPS005-01 — High

There is no shipped Document Verify operation that composes the named evidence engines.

The approved `/document-verify` live adapter explicitly states that MRZ, barcode, OCR and NFC are
unconnected placeholders. It calls `/api/v1/documents/mrz`, `/barcode`, `/ocr` and `/nfc`, but none
of those endpoints exists. The API host references BIO-DOC-001, BIO-DOC-009, BIO-DOC-010 and
BIO-DOC-011 only; it does not reference the BIO-DOC-002/003/004/007 engine projects. Its only live
Document Verify operations are the synthetic authenticity fixture and template-pack identification.

The frontend `EvidenceStepResult` also has no provenance field and makes confidence optional, so
even a future successful response could be silently reduced to state/message rather than satisfying
AC01. The UI's mismatch/manual-review states are deterministic mock scenarios, not results composed
from raw engine evidence.

Consequently, the isolated libraries demonstrate useful primitives but cannot prove AC01–AC05 in
one operation. In particular, there is no executable boundary at which QA can show that a real
mismatch survives orchestration, unsupported NFC remains not performed, manual review preserves
the originating raw records, or PERSON data changes only through an explicit proposal.

## Independent checks

- AC02 mismatch preservation in BIO-DOC-008: PASS at component level.
- AC03 unsupported NFC policy in BIO-DOC-009: PASS at component level.
- AC04/AC05 non-destructive proposal model in BIO-DOC-001: PASS at component level.
- API host references for MRZ/barcode/OCR/NFC engines: 0/4.
- Live MRZ/barcode/OCR/NFC endpoints: 0/4.
- Shipped UI provenance field: absent.
- Required confidence for every engine result: absent.

Required remediation: add a Kemi-approved Document Verify coordinator and live API contract that
owns a session, invokes the real engines, appends immutable raw evidence with source/provenance and
confidence, computes comparison/authenticity from those exact records, records review separately,
and creates explicit PERSON change proposals. Connect the existing UI port without redesigning the
route. Add an end-to-end synthetic test covering success, source mismatch, unavailable NFC, manual
review, rejected/accepted PERSON proposal, replay, and concurrent review.
