// BIO-MOB-002-AC03: retry is exponential and bounded. No ADR or the master
// spec names concrete numbers (see retry_policy.dart's file doc comment for
// what was checked) — these tests exercise the documented algorithm
// (AWS-style full jitter) and this story's own chosen constants directly.
import 'dart:math';

import 'package:biolog_mobile/core/offline/retry_policy.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('hasAttemptsRemaining', () {
    test('true while below maxAttempts, false once reached (bounded)', () {
      final policy = RetryPolicy(maxAttempts: 3);
      expect(policy.hasAttemptsRemaining(0), isTrue);
      expect(policy.hasAttemptsRemaining(1), isTrue);
      expect(policy.hasAttemptsRemaining(2), isTrue);
      expect(policy.hasAttemptsRemaining(3), isFalse);
      expect(policy.hasAttemptsRemaining(10), isFalse);
    });
  });

  group('nextDelay', () {
    test('never exceeds the exponentially-growing ceiling, capped at maxDelay', () {
      final policy = RetryPolicy(
        baseDelay: const Duration(seconds: 10),
        maxDelay: const Duration(minutes: 5),
        maxAttempts: 10,
      );
      // A fixed-seed Random makes this deterministic without asserting an
      // exact value (full jitter is intentionally random) — only the bound.
      final rng = Random(42);
      for (var attempt = 0; attempt < 10; attempt++) {
        final ceilingMs = min(
          10000 * pow(2, attempt).toDouble(),
          const Duration(minutes: 5).inMilliseconds.toDouble(),
        );
        final delay = policy.nextDelay(attempt, random: rng);
        expect(delay.inMilliseconds, greaterThanOrEqualTo(0));
        expect(delay.inMilliseconds, lessThanOrEqualTo(ceilingMs.round()));
      }
    });

    test('the ceiling grows exponentially before hitting the cap', () {
      final policy = RetryPolicy(
        baseDelay: const Duration(seconds: 1),
        maxDelay: const Duration(hours: 1),
        maxAttempts: 20,
      );
      // Force jitter to its maximum (1.0) so the returned delay equals the
      // ceiling exactly, making the doubling pattern directly observable.
      final alwaysMaxJitter = _FixedRandom(1.0);
      final d0 = policy.nextDelay(0, random: alwaysMaxJitter);
      final d1 = policy.nextDelay(1, random: alwaysMaxJitter);
      final d2 = policy.nextDelay(2, random: alwaysMaxJitter);
      expect(d0.inMilliseconds, equals(1000));
      expect(d1.inMilliseconds, equals(2000));
      expect(d2.inMilliseconds, equals(4000));
    });

    test('is capped at maxDelay however many attempts have failed', () {
      final policy = RetryPolicy(
        baseDelay: const Duration(seconds: 1),
        maxDelay: const Duration(minutes: 1),
        maxAttempts: 50,
      );
      final alwaysMaxJitter = _FixedRandom(1.0);
      final farOut = policy.nextDelay(40, random: alwaysMaxJitter);
      expect(farOut.inMilliseconds, equals(const Duration(minutes: 1).inMilliseconds));
    });

    test('full jitter can return a delay of (near) zero, not a fixed minimum', () {
      final policy = RetryPolicy(baseDelay: const Duration(seconds: 30));
      final alwaysZeroJitter = _FixedRandom(0.0);
      final delay = policy.nextDelay(3, random: alwaysZeroJitter);
      expect(delay.inMilliseconds, equals(0));
    });
  });

  group('defaults', () {
    test('are a bounded, sensible policy out of the box', () {
      final policy = RetryPolicy();
      expect(policy.maxAttempts, greaterThan(0));
      expect(policy.baseDelay, greaterThan(Duration.zero));
      expect(policy.maxDelay, greaterThanOrEqualTo(policy.baseDelay));
    });
  });
}

/// A [Random] whose `nextDouble()` always returns the same fixed value, for
/// deterministically observing the deterministic half of an otherwise
/// jittered computation.
class _FixedRandom implements Random {
  _FixedRandom(this._value);

  final double _value;

  @override
  double nextDouble() => _value;

  @override
  int nextInt(int max) => (_value * max).floor();

  @override
  bool nextBool() => _value >= 0.5;
}
