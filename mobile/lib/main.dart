// AC01/AC05: entry point boots against the real encrypted local store and
// real connectivity signal. No production data is bundled or read here —
// the store starts empty on every fresh install.
import 'package:flutter/material.dart';

import 'app.dart';
import 'core/connectivity/plugin_connectivity_signal.dart';
import 'core/db/sqlcipher_app_database.dart';
import 'core/session/session_controller.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final session = SessionController(
    database: SqlCipherAppDatabase(),
    connectivity: PluginConnectivitySignal(),
  );
  await session.bootstrap();
  runApp(BiologApp(session: session));
}
