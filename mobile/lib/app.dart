// App root: wires the session controller into the widget tree and hosts
// go_router. AC01: this widget must build and boot without throwing on a
// device/emulator with an empty local store.
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'core/session/session_controller.dart';
import 'navigation/app_router.dart';

class BiologApp extends StatefulWidget {
  const BiologApp({super.key, required this.session});

  final SessionController session;

  @override
  State<BiologApp> createState() => _BiologAppState();
}

class _BiologAppState extends State<BiologApp> {
  late final router = buildAppRouter(widget.session);

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider<SessionController>.value(
      value: widget.session,
      child: MaterialApp.router(
        title: 'BIOLOG',
        debugShowCheckedModeBanner: false,
        theme: ThemeData(colorSchemeSeed: const Color(0xFFF4502A), useMaterial3: true),
        routerConfig: router,
      ),
    );
  }
}
