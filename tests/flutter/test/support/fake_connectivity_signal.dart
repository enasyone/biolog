// Test double for ConnectivitySignal. connectivity_plus needs a real
// platform channel; tests control connectivity deterministically instead.
library;

import 'dart:async';

import 'package:biolog_mobile/core/connectivity/connectivity_signal.dart';

class FakeConnectivitySignal implements ConnectivitySignal {
  FakeConnectivitySignal({ConnectivityState initial = ConnectivityState.online})
      : _current = initial;

  ConnectivityState _current;
  final StreamController<ConnectivityState> _controller =
      StreamController<ConnectivityState>.broadcast();

  @override
  ConnectivityState get current => _current;

  @override
  Stream<ConnectivityState> get onChange => _controller.stream;

  @override
  Future<void> start() async {
    _controller.add(_current);
  }

  void set(ConnectivityState state) {
    _current = state;
    _controller.add(state);
  }

  @override
  void dispose() {
    _controller.close();
  }
}
