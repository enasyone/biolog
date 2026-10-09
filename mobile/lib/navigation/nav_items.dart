// Configured navigation surface for the mobile shell (AC04). Mirrors the
// web shell's config-driven nav pattern (`web/lib/nav-items.ts` +
// `filterNavByPermission`, BIO-FND-007): the router and every nav widget are
// built from this single list rather than hard-coding routes in multiple
// places, so navigation stays "as configured" instead of scattered.
library;

import 'package:flutter/material.dart';

/// One entry in the app's configured navigation. [route] is a go_router
/// path, always relative to the authenticated shell root.
class NavItem {
  const NavItem({
    required this.id,
    required this.label,
    required this.route,
    required this.icon,
  });

  final String id;
  final String label;
  final String route;
  final IconData icon;
}

/// The navigation surface required by BIO-MOB-001-AC04: enrolment, verify,
/// QR/barcode scan, bus and the offline queue state. Feature stories may
/// extend this list; they must not bypass it with ad-hoc routes.
const List<NavItem> configuredNavItems = [
  NavItem(id: 'enrolment', label: 'Enrolment', route: '/enrolment', icon: Icons.person_add_alt_1),
  NavItem(id: 'verify', label: 'Verify', route: '/verify', icon: Icons.verified_user),
  NavItem(id: 'scan', label: 'QR / Barcode', route: '/scan', icon: Icons.qr_code_scanner),
  NavItem(id: 'bus', label: 'Bus', route: '/bus', icon: Icons.directions_bus),
  NavItem(id: 'offline', label: 'Offline queue', route: '/offline', icon: Icons.cloud_off),
];
