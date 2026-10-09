// AC04: an explicit offline state, always visible, never silent. Shown in
// the shell app bar whenever the device believes it has no connectivity, and
// again as the dedicated "Offline queue" screen with the pending count.
library;

import 'package:flutter/material.dart';

import '../../core/connectivity/connectivity_signal.dart';

class OfflineBanner extends StatelessWidget {
  const OfflineBanner({
    super.key,
    required this.state,
    required this.pendingMutationCount,
  });

  final ConnectivityState state;
  final int pendingMutationCount;

  @override
  Widget build(BuildContext context) {
    if (state != ConnectivityState.offline) return const SizedBox.shrink();
    return Container(
      key: const Key('offline-banner'),
      width: double.infinity,
      color: Theme.of(context).colorScheme.errorContainer,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: Row(
        children: [
          const Icon(Icons.cloud_off, size: 18),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              pendingMutationCount > 0
                  ? 'Offline — $pendingMutationCount change(s) queued locally'
                  : 'Offline — working from local data',
            ),
          ),
        ],
      ),
    );
  }
}
