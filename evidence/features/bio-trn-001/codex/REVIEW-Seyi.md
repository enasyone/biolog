# BIO-TRN-001 — Seyi independent review

**Builder commit:** `fa2a0e3` (evidence `cfa4bfb`)  
**Verdict:** PASS

## Checks

- Ran the feature structural harness independently: **34/34 PASS**. It verifies route/vehicle/stop lifecycle invariants, assignment overlap protection, authorization/audit, device linkage, campus/expiry rules, live routes, and untouched transport UI.
- Confirmed the overlap lock is keyed by `(tenant, person)` and acquired before construction; the documented 20-way race test proves only one overlapping assignment succeeds.
- Confirmed `IDeviceExistenceGateway` wraps BIO-DEV-001's real `DeviceService` and checks existence without status-gating; the real-host test registers and links an actual device.
- Confirmed AC05 is honestly HYBRID: live bridge routes exercise real registry/assignment logic, while boarding/alighting display remains a disclosed sibling-story scope.
- Reviewed the reported repeatable targeted results: 26/26 feature, 6/6 host-channel, 158/158 integration, 398/398 frontend unit. The full-gate Playwright failures are documented as unrelated flakes on `/audit` and `/cards`, never `/transport`.
