// Real ConnectivitySignal backed by `connectivity_plus`.
library;

import 'dart:async';

import 'package:connectivity_plus/connectivity_plus.dart';

import 'connectivity_signal.dart';

ConnectivityState _fromResults(List<ConnectivityResult> results) {
  if (results.isEmpty || results.every((r) => r == ConnectivityResult.none)) {
    return ConnectivityState.offline;
  }
  return ConnectivityState.online;
}

class PluginConnectivitySignal implements ConnectivitySignal {
  PluginConnectivitySignal({Connectivity? connectivity})
      : _connectivity = connectivity ?? Connectivity();

  final Connectivity _connectivity;
  final StreamController<ConnectivityState> _controller =
      StreamController<ConnectivityState>.broadcast();
  StreamSubscription<List<ConnectivityResult>>? _subscription;
  ConnectivityState _current = ConnectivityState.unknown;

  @override
  ConnectivityState get current => _current;

  @override
  Stream<ConnectivityState> get onChange => _controller.stream;

  @override
  Future<void> start() async {
    _current = _fromResults(await _connectivity.checkConnectivity());
    _controller.add(_current);
    _subscription = _connectivity.onConnectivityChanged.listen((results) {
      _current = _fromResults(results);
      _controller.add(_current);
    });
  }

  @override
  void dispose() {
    _subscription?.cancel();
    _controller.close();
  }
}
