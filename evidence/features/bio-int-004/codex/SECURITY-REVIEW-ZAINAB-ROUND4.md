# BIO-INT-004 security review — Zainab round 4

**Verdict: FAIL**  
**Reviewed head:** `9cc123b`  
**Remediation:** `ce59642`

## Closed from round 3

- The completed claim now retains the exact terminal outcome, attempt and detail.
- A single audit failure self-heals in the same dispatch; repeated failures can converge during a
  later replay without resending.
- The four omitted current IPv6 special-purpose prefixes from round 3 are now blocked and tested.
- The checked-in address table is honestly dated rather than described as permanently complete.

## Blocking findings

### ZB-INT004-07 — Medium — concurrent audit repair is another check-then-write race

`RepairTerminalRecordAsync` calls `ListForTenantAsync`, checks with `Any`, then separately calls
`RecordAsync`. The audit contract has no atomic create-if-absent operation. Two concurrent replay
dispatches for the same completed claim can both observe the terminal record absent and both
append an identical `Delivered` or `DeadLettered` row.

This corrupts AC04's one-row-per-attempt trail and repeats the same check-then-act pattern removed
from delivery deduplication, now in repair. Existing tests exercise sequential repair only.

**Required remediation:** give terminal audit records a stable structural identity and expose an
atomic immutable create-if-absent operation, or serialize every repair for the same
`(tenant, subscription, event, attempt, outcome)` key. Add a barrier-gated concurrent replay test
that starts from a completed claim with a missing terminal record and proves exactly one terminal
row is created.

### ZB-INT004-08 — High — reserved IPv6 space outside `2000::/3` is still allowed

The table rejects current special-purpose entries but `IsAllowed` returns true for every address
outside that table. That is not a positive global-unicast policy. IANA's IPv6 address-space
registry currently identifies `2000::/3` as the assignable global-unicast block and reserves other
large blocks. For example, `4000::1`, `6000::1`, `8000::1`, `a000::1`, and `c000::1` all pass the
current policy.

Reserved space can be routed internally even though it is not a public destination, so a tenant
can still target an internal service on such an address. Blocking selected special-purpose rows
does not close that server-side request forgery path.

**Required remediation:** for IPv6, first require membership in the currently assigned global
unicast space (`2000::/3` for this registry snapshot), then reject the non-globally-reachable
special-purpose exceptions inside it. Add representative tests for every reserved top-level IPv6
block and boundary tests around `2000::/3`. Apply the same positive-public-routing principle to
IPv4 rather than assuming absence from the special-purpose table guarantees public allocation.

## Verification

- Inspected the complete `e41fbdb..ce59642` production and test delta.
- Reviewed terminal-result persistence, same-call and replay repair, audit sink semantics, and
  all address-table additions.
- Compared the policy with the IANA IPv6 Address Space and IPv6 Special-Purpose registries.
- Builder evidence reports 91/91 feature, 241/241 integration, and 210/210 unit tests. The suite
  does not cover concurrent repair or reserved IPv6 space outside `2000::/3`.
