# BIO-INT-004 independent review — Seyi

**Verdict: FAIL**

Reviewed head `e41fbdb` (round-2 remediation `01ec2af`) against BIO-INT-004 AC01–AC05 and the prior security findings. The focused feature assembly passes 81/81, but two required failure/recovery and security cases remain incorrect.

## Findings

### SY-INT004-01 — High — terminal audit failure is never repaired

AC04 requires retry/dead-letter behaviour to remain auditable. `CompleteTerminalAsync` marks the idempotency record completed before writing the terminal audit (`WebhookDispatchService.cs:233-250`). If that write fails and the recheck confirms it did not land, the method throws. A later dispatch sees `Completed` and writes only a new `Deduplicated` row (`WebhookDispatchService.cs:109-116`); it never reconstructs or retries the missing `Delivered`/`DeadLettered` row.

The new regression explicitly demonstrates the gap: after `FlakyAuditSink` drops the Delivered record, it asserts only that the next call is deduplicated and the sender remains single-call (`WebhookDispatchServiceTests.cs:441-466`). It never asserts that the terminal audit trail converges, because it does not: the durable outcome remains absent and the later Deduplicated row cannot prove whether the original result was Delivered or DeadLettered, its attempt number, or its detail.

This closes duplicate sending but still violates AC04 and the prior required remediation's “repair the missing audit without resending” requirement.

**Required remediation:** retain enough terminal outcome data in the completed claim and retry/repair the exact missing terminal audit record on replay or via a recovery worker. Add a regression that first drops the terminal audit, then proves one send total **and** eventual presence of the original terminal outcome with its attempt/detail.

### SY-INT004-02 — High — the claimed complete special-purpose registry still permits omitted IPv6 internal-use space

`WebhookDestinationPolicy.SpecialPurposeRanges` claims to be the complete IANA special-purpose registry and treats every unlisted address as global unicast (`WebhookDestinationPolicy.cs:92-126,136-153`). The table is incomplete. In particular it contains `64:ff9b::/96` but omits the IANA local-use NAT64 translation prefix `64:ff9b:1::/48`; it also omits newer/special IPv6 allocations such as `3fff::/20` and `5f00::/16`. The IPv4 table likewise omits registry entries including `192.31.196.0/24`, `192.52.193.0/24`, and `192.175.48.0/24`.

Because `64:ff9b:1::/48` is specifically for local-use translation, allowing it at the connect-time Server-Side Request Forgery boundary can route a webhook toward internal translated destinations on networks that deploy that prefix. The current tests enumerate only the ranges copied into the implementation and therefore cannot catch the omission.

**Required remediation:** derive the policy from an authoritative maintained global-unicast/public-routability source, or add every current special-purpose range and regression vectors including `64:ff9b:1::1`, `3fff::1`, and `5f00::1`. Keep the existing connect-time and redirect-hop enforcement.

## Category verdicts

- Functional: **FAIL** — terminal delivery history can remain incomplete.
- Security: **FAIL** — incomplete destination policy leaves a Server-Side Request Forgery path through local-use IPv6 translation space.
- Tenant isolation: **PASS** — subscription keys, list/match operations, and management permissions are tenant-scoped.
- Offline/recovery: **FAIL** — terminal audit recovery does not converge.
- Concurrency: **PASS** — the atomic claim and deterministic owner/loser barrier close the duplicate concurrent-send race.
- Regression: **PASS** for the focused assembly, 81/81.

No production code was changed by this review.
