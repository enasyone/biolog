# BIO-INT-004 independent review — remediation recheck

**Verdict: FAIL**

Reviewed implementation `ce59642` / evidence `9cc123b`. The prior audit-convergence and special-address omissions are closed for sequential execution: the completed claim now retains the terminal outcome/attempt/detail, replay repairs the missing terminal record without resending, and the named IPv6 ranges are blocked. A new concurrency gap remains in that repair path.

## Blocking finding

### SY-INT004-03 — High — concurrent recovery appends duplicate terminal audit records

`RepairTerminalRecordAsync` performs a non-atomic `ListForTenantAsync` existence check followed by `RecordAsync` (`WebhookDispatchService.cs:279-297`). `InMemoryWebhookDeliveryAuditSink.RecordAsync` is an unconditional list append and exposes no create-if-absent operation.

After a delivered event's original terminal audit and immediate repair both fail, the idempotency claim is correctly Completed. If two duplicate dispatches then arrive concurrently, both read the same completed claim, both list an empty audit trail, both decide the terminal record is missing, and both append the same Delivered record. Each then also appends its own Deduplicated observation.

The result is two indistinguishable terminal delivery records for one actual send, contradicting the remediation's stated “one durable terminal record” invariant and making the AC04 audit trail overcount delivery. The new tests exercise only one sequential recovery caller, so they cannot discriminate this race.

**Required remediation:** make terminal audit repair atomic for `(tenant_id,event_id,subscription_id,attempt,outcome)`—for example an audit-sink `CreateIfAbsentAsync`/stable event-id operation or a per-delivery lock spanning existence check and write. Add a barrier regression that forces two repair callers past the missing-record observation before either writes, then proves one send and exactly one terminal audit record.

## Category verdicts

- Functional: **FAIL** — recovery can overstate one delivery as two terminal records.
- Security: **PASS** — HTTPS and connect-time special-address policy now cover the reported Server-Side Request Forgery gaps.
- Tenant isolation: **PASS**.
- Offline/recovery: **FAIL** — concurrent repair is non-atomic.
- Concurrency: **FAIL** — check-then-write terminal repair race.
- Regression: focused feature assembly **91/91 PASS**.

No production code was changed by this review.
