// The authenticated shell: shows the explicit tenant (AC03), the offline
// banner (AC04) and configured navigation (AC04) around whichever feature
// screen go_router has routed to.
library;

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../core/session/session_controller.dart';
import '../../navigation/nav_items.dart';
import '../offline/offline_banner.dart';

class AppShellScreen extends StatelessWidget {
  const AppShellScreen({super.key, required this.currentRoute, required this.child});

  final String currentRoute;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final session = context.watch<SessionController>();
    final tenant = session.tenantContext;
    final selectedIndex = configuredNavItems.indexWhere((i) => i.route == currentRoute);

    return Scaffold(
      appBar: AppBar(
        title: Text(tenant == null ? 'BIOLOG' : 'BIOLOG · ${tenant.bindingKey}'),
        actions: [
          IconButton(
            key: const Key('sign-out-button'),
            tooltip: 'Change tenant',
            icon: const Icon(Icons.logout),
            onPressed: () => context.read<SessionController>().signOut(),
          ),
        ],
      ),
      body: Column(
        children: [
          OfflineBanner(
            state: session.connectivityState,
            pendingMutationCount: session.pendingMutationCount,
          ),
          Expanded(child: child),
        ],
      ),
      bottomNavigationBar: NavigationBar(
        key: const Key('app-nav-bar'),
        selectedIndex: selectedIndex < 0 ? 0 : selectedIndex,
        onDestinationSelected: (index) {
          context.go(configuredNavItems[index].route);
        },
        destinations: [
          for (final item in configuredNavItems)
            NavigationDestination(icon: Icon(item.icon), label: item.label),
        ],
      ),
    );
  }
}
