# BIO-INT-002 round-five review

Verdict: PASS

Reviewed `f910ece` / `90e4055`.

- Claim ownership now encloses lock acquisition, the fresh mapping read, direction/active guards, row work, audit, and persistence in one release-on-failure boundary. Disabled and direction-flipped post-claim paths release cleanly; their same-key retries succeed after configuration is restored.
- Row provenance is scoped by tenant, idempotency key, mapping, and external key, preventing cross-mapping replay. Duplicate external keys are rejected before either preview work or claim acquisition, preventing within-request row aliasing.
- Provenance recording uses `CancellationToken.None`, verifies ambiguous failure by reading the exact key, and rethrows a confirmed failure so the attempt releases instead of completing with a provenance gap.
- Earlier orphan-recording, content-aware adoption, claim ownership, and fresh post-lock mapping checks remain intact.
- Independent structural harness: 67 passed, 0 failed.
- The focused test project compiled. Local execution was blocked by the known VSTest loopback-socket restriction; builder evidence reports 38/38 feature and 192/192 integration tests.

No blocking finding.
