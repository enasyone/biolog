// Abstraction over device connectivity so the offline-state UI (AC04) and
// the future sync worker (BIO-MOB-002/003) can react to it without coupling
// directly to `connectivity_plus` (and so widget tests can inject a fake
// signal instead of depending on a real platform channel).
library;

/// Whether this device currently believes it has network reachability.
/// `unknown` is used only for the instant before the first real reading
/// arrives — the UI must never default it to `online`.
enum ConnectivityState { online, offline, unknown }

abstract class ConnectivitySignal {
  /// Best-known current state.
  ConnectivityState get current;

  /// Emits whenever the state changes.
  Stream<ConnectivityState> get onChange;

  Future<void> start();

  void dispose();
}
