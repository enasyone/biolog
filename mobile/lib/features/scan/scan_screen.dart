// Navigable placeholder for QR/barcode scanning (AC04). Camera-backed
// scanning is delivered by a later feature story; this shell provides the
// route only.
library;

import 'package:flutter/material.dart';

import '../shared/feature_placeholder.dart';

class ScanScreen extends StatelessWidget {
  const ScanScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return const FeaturePlaceholder(
      title: 'QR / Barcode',
      description:
          'QR and barcode scanning is delivered by a later feature story. '
          'This shell provides the route and tenant/offline-aware '
          'scaffolding it will run inside.',
      icon: Icons.qr_code_scanner,
    );
  }
}
