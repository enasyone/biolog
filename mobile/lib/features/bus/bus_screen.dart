// Navigable placeholder for the bus (transport check-in) workflow (AC04).
// Delivered by a later feature story; this shell provides the route only.
library;

import 'package:flutter/material.dart';

import '../shared/feature_placeholder.dart';

class BusScreen extends StatelessWidget {
  const BusScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const FeaturePlaceholder(
      title: 'Bus',
      description:
          'Bus check-in is delivered by a later feature story. This shell '
          'provides the route and tenant/offline-aware scaffolding it will '
          'run inside.',
      icon: Icons.directions_bus,
    );
  }
}
