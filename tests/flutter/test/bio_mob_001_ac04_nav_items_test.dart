// BIO-MOB-001-AC04: navigation supports enrolment, verify, QR/barcode, bus
// and offline states "as configured" — driven from one config list rather
// than scattered route literals (mirrors the web shell's nav-items
// pattern, BIO-FND-007).
import 'package:biolog_mobile/navigation/nav_items.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('configured navigation covers every AC04-required state exactly once', () {
    final ids = configuredNavItems.map((i) => i.id).toList();
    expect(ids, containsAll(['enrolment', 'verify', 'scan', 'bus', 'offline']));
    expect(ids.toSet().length, equals(ids.length), reason: 'no duplicate nav items');
  });

  test('every nav item has a distinct, absolute go_router path', () {
    final routes = configuredNavItems.map((i) => i.route).toList();
    expect(routes.toSet().length, equals(routes.length));
    for (final route in routes) {
      expect(route, startsWith('/'));
    }
  });
}
