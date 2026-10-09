# BIO-INT-004 security review — Zainab round 2

**Verdict: FAIL**  
**Reviewed head:** `8cb7c01`  
**Remediation:** `911394e`

## Closed from round 1

- The connect callback removes the DNS-resolution time-of-check/time-of-use gap and applies to
  redirect connections. IPv4-mapped IPv6, loopback, RFC1918, link-local/metadata, RFC6598,
  multicast, and IPv6 unique-local addresses are rejected before a socket is opened.
- The atomic claim correctly permits one concurrent owner for a structural
  `(tenant, subscription, event)` key. The barrier and mutant tests discriminate the old
  check-then-send race.
- Terminal audit calls no longer reuse caller cancellation.

## Blocking findings

### ZB-INT004-03 — High — send-success/audit-failure still releases the claim and redelivers

After `sender.SendAsync` reports `Delivered`, `AttemptWithRetryAsync` writes the delivery audit
before returning (`WebhookDispatchService.cs:201-204`). If that audit sink throws for any reason
other than caller cancellation, the enclosing catch releases the in-flight idempotency claim
(`WebhookDispatchService.cs:135-142`). A later dispatch wins a new claim and sends the same event
again.

This is the exact receiver-accepted/completion-write-failed ambiguity called out in round 1. Using
`CancellationToken.None` closes cancellation only; it does not make the audit write infallible or
durable. The new tests include a cancellation-aware sink but no sink that throws after the sender
has accepted the event, so they miss this path.

**Required remediation:** do not release a claim once the send may have been accepted. Use a
durable delivery state/outbox with recovery, or complete a durable claim independently of the
fallible audit trail and repair the missing audit without resending. Carry a stable delivery
identifier that receivers can enforce if ambiguous network outcomes are retried. Add a regression
where send returns `Delivered`, terminal audit throws, and a second dispatch proves the sender is
still called exactly once while audit recovery converges.

### ZB-INT004-04 — High — outbound confidentiality and destination policy remain incomplete

The API still accepts `http://` subscriptions, while the event envelope explicitly permits
`personal` and `sensitive` payloads. HMAC signing provides integrity/authenticity, not transport
confidentiality; a network observer can read the payload and capture a valid signed request.

The address blocklist is also not a global-unicast allow policy. It allows reserved/non-public
ranges including `198.18.0.0/15` (benchmark networks frequently used inside labs),
`192.0.0.0/24`, `192.0.2.0/24`, `198.51.100.0/24`, and `203.0.113.0/24`, plus special IPv6 ranges.
An internal service using an allowed special-purpose range remains reachable. Tests cover only a
subset of named private ranges and therefore do not expose this gap.

**Required remediation:** require HTTPS outside an explicit local-development mode and use a
maintained positive definition of publicly routable/global-unicast destinations, rejecting all
special-purpose ranges for IPv4, IPv6, and mapped forms. Add representative benchmark,
documentation, protocol-assignment, and special IPv6 test vectors. The current connect-time and
per-redirect enforcement point should be retained.

## Verification

- Inspected the complete `1b98b87..8cb7c01` production and test delta.
- Reviewed `WebhookDestinationPolicy`, `WebhookConnectCallback`, `HttpWebhookSender`, the new
  idempotency store, dispatch ordering, barrier/mutant proof, cancellation proof, and builder
  evidence.
- Builder evidence reports 54/54 feature, 241/241 integration, and 210/210 unit tests. Those
  passing tests do not exercise the two remaining adversarial paths above.
