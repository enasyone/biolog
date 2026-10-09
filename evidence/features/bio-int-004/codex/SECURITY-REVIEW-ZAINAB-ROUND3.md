# BIO-INT-004 security review — Zainab round 3

**Verdict: FAIL**  
**Reviewed head:** `e41fbdb`  
**Remediation:** `01ec2af`

## Closed from round 2

- Completing the claim before the terminal audit, plus conditional release, prevents a
  post-send audit exception from reopening the claim. The new regression genuinely proves one
  send across the failing first dispatch and its replay.
- Plain HTTP is default-denied and requires the dedicated `Webhooks:AllowInsecureHttp` opt-in.
- The address policy now covers the IPv4 gaps named in round 2 and several additional IPv6
  special-purpose ranges with tested non-byte-aligned CIDR matching.

## Blocking findings

### ZB-INT004-05 — Medium — audit failure is made non-replayable but never repaired

`CompleteTerminalAsync` marks the claim complete, attempts the terminal audit, rechecks once,
then throws `WebhookAuditUnavailableException` if the record is absent. A later dispatch sees
the completed claim and writes only a `Deduplicated` record. It never recreates the missing
`Delivered` or `DeadLettered` attempt.

The new regression explicitly leaves the real audit sink empty after the accepted send and only
asserts no second send. That closes replay safety but not AC04's auditable delivery/dead-letter
requirement or round 2's requirement that audit recovery converge. The final trail can say
"deduplicated" without any record of what was supposedly deduplicated.

**Required remediation:** retain enough immutable terminal metadata with the completed claim to
idempotently create/repair the exact terminal audit record. On a completed replay, repair that
record before adding an optional dedup observation. Add a fail-once audit regression that ends
with one send and one durable terminal record carrying the original outcome/attempt.

### ZB-INT004-06 — High — the claimed complete IANA destination policy is incomplete

`SpecialPurposeRanges` omits current non-globally-reachable IPv6 entries, including:

- `64:ff9b:1::/48` — local-use IPv4/IPv6 translation;
- `100:0:0:1::/64` — dummy IPv6 prefix;
- `3fff::/20` — documentation;
- `5f00::/16` — Segment Routing IPv6 SIDs, not globally reachable.

The local-use NAT64 prefix is especially security-relevant: a deployment routing it can translate
an apparently allowed IPv6 destination into IPv4 space the policy intended to block. The table
also calls itself complete and assumes every address outside it is global unicast, which is not a
safe invariant as the registry evolves.

**Required remediation:** add all current IANA entries whose `Globally Reachable` value is false
or not affirmative, including the prefixes above, with tests. Prefer generating/versioning this
data from the authoritative registry or validating the checked-in table against a pinned registry
snapshot so future allocations cannot silently reopen the policy.

## Verification

- Inspected the complete `8cb7c01..01ec2af` production and test delta.
- Checked claim completion/release ordering, audit recheck behavior, HTTP configuration default,
  CIDR matching, and every checked-in IPv4/IPv6 range.
- Compared the IPv6 table with the current IANA IPv6 Special-Purpose Address Registry.
- Builder evidence reports 81/81 feature, 241/241 integration, and 210/210 unit tests. The suite
  does not cover missing terminal-audit convergence or the omitted IPv6 ranges.
