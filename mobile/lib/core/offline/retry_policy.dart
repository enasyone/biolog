// BIO-MOB-002-AC03: retry is exponential and bounded. Neither the master
// spec nor any accepted ADR names concrete numbers (checked ADR-0001,
// ADR-0003, ADR-0004, `contracts/js/*.mjs`,
// `02-ARCHITECTURE/OFFLINE-EDGE-ARCHITECTURE.md`/`EVENT-ARCHITECTURE.md`;
// none specify max attempts, base delay or jitter) — only the requirement
// itself and, from `OFFLINE-EDGE-ARCHITECTURE.md`, that "reconnect storms
// must be rate-controlled without losing or duplicating events". The
// constants and algorithm below are this story's own documented choice,
// not a value read from a contract.
//
// Algorithm is AWS's published "Full Jitter" backoff
// (https://aws.amazon.com/blogs/architecture/exponential-backoff-and-jitter/):
// `sleep = random_between(0, min(cap, base * 2^attempt))`. Chosen over a
// fixed or additive-jitter schedule specifically because it is the
// documented, named answer to "rate-controlled without losing or
// duplicating events" — every device reconnecting after an outage draws an
// independent random delay instead of a synchronised one, which is exactly
// what avoids a reconnect storm.
library;

import 'dart:math';

/// Bounded exponential backoff with full jitter for offline-mutation upload
/// retries (BIO-MOB-002-AC03).
class RetryPolicy {
  RetryPolicy({
    this.baseDelay = const Duration(seconds: 30),
    this.maxDelay = const Duration(minutes: 30),
    this.maxAttempts = 8,
  })  : assert(maxAttempts > 0),
        assert(baseDelay > Duration.zero),
        assert(maxDelay >= baseDelay);

  /// Delay before the first retry (attempt 0 -> 1).
  final Duration baseDelay;

  /// Backoff never exceeds this, however many attempts have failed.
  final Duration maxDelay;

  /// A mutation is retried at most this many times before moving to the
  /// terminal `failed` sync state — "bounded" (AC03).
  final int maxAttempts;

  /// True while [retryCount] failed attempts have not yet exhausted the
  /// bound; false means the next failure is terminal.
  bool hasAttemptsRemaining(int retryCount) => retryCount < maxAttempts;

  /// The delay to wait before the attempt numbered [retryCount] (0-based:
  /// the delay before the *first* retry, after the first failure, is
  /// `nextDelay(0)`). Full-jitter: uniformly random between zero and the
  /// exponentially-growing, capped ceiling — never a fixed value, so
  /// concurrently-failing devices do not retry in lockstep.
  Duration nextDelay(int retryCount, {Random? random}) {
    final rng = random ?? Random.secure();
    final ceilingMs = min(
      baseDelay.inMilliseconds * pow(2, retryCount).toDouble(),
      maxDelay.inMilliseconds.toDouble(),
    );
    final jitteredMs = rng.nextDouble() * ceilingMs;
    return Duration(milliseconds: jitteredMs.round());
  }
}
