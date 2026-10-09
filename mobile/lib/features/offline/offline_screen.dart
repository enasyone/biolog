// AC04 dedicated offline-state screen: shows the local queue depth and lets
// the operator confirm the device is intentionally working offline. Upload
// behaviour is out of scope for this story (BIO-MOB-002/003 own sync).
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/connectivity/connectivity_signal.dart';
import '../../core/session/session_controller.dart';

class OfflineScreen extends StatelessWidget {
  const OfflineScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final session = context.watch<SessionController>();
    final isOffline = session.connectivityState == ConnectivityState.offline;
    return Scaffold(
      appBar: AppBar(title: const Text('Offline queue')),
      body: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              isOffline ? 'Device is offline' : 'Device is online',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 8),
            Text(
              'Every change made while offline is queued locally and never '
              'lost. Sync of queued changes is delivered by a later story.',
            ),
            const SizedBox(height: 24),
            Text(
              'Pending mutations: ${session.pendingMutationCount}',
              key: const Key('pending-mutation-count'),
              style: Theme.of(context).textTheme.headlineSmall,
            ),
          ],
        ),
      ),
    );
  }
}
