// BIO-MOB-001-AC01: app boots locally/emulator. This pumps the real
// `BiologApp` widget tree (real screens, real router, real session
// controller) against injected fakes for the two platform-channel
// dependencies (encrypted store, connectivity) that only work on a real
// device. Also exercises AC03 and AC04 through the widget tree, since they
// gate what "booting" means here.
//
// BIO-MOB-001-QA-02 (Chioma, acceptance round 2): the tenant-select screen
// no longer has a form — there is no production path that turns typed input
// into a bound session (see tenant_context.dart's file doc comment). The
// second and third tests below pre-seed an already-"resolved" context via
// `syntheticTenantContext` (a stand-in for a future trusted resolution
// boundary, ADR-0002 Decision 6) purely to exercise the shell/nav/offline UI
// that sits behind the tenant gate — they are not evidence that this app can
// reach that state on its own today.
import 'package:biolog_mobile/app.dart';
import 'package:biolog_mobile/core/connectivity/connectivity_signal.dart';
import 'package:biolog_mobile/core/session/session_controller.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_app_database.dart';
import 'support/fake_connectivity_signal.dart';
import 'support/synthetic_tenant_context.dart';

void main() {
  testWidgets('boots to an informational, unbound tenant-select screen with no form',
      (tester) async {
    final session = SessionController(
      database: FakeAppDatabase(),
      connectivity: FakeConnectivitySignal(),
    );
    await session.bootstrap();

    await tester.pumpWidget(BiologApp(session: session));
    await tester.pumpAndSettle();

    expect(find.text('Tenant resolution is not yet available on this device'), findsOneWidget);
    expect(find.byType(TextFormField), findsNothing);
    expect(find.byKey(const Key('app-nav-bar')), findsNothing);
  });

  testWidgets(
      'a context resolved by a trusted boundary unlocks the configured navigation shell (AC03/AC04)',
      (tester) async {
    final database = FakeAppDatabase();
    await database.open();
    await database.saveTenantContext(syntheticTenantContext());

    final session = SessionController(database: database, connectivity: FakeConnectivitySignal());
    await session.bootstrap();

    await tester.pumpWidget(BiologApp(session: session));
    await tester.pumpAndSettle();

    expect(find.text('Tenant resolution is not yet available on this device'), findsNothing);
    expect(find.byKey(const Key('app-nav-bar')), findsOneWidget);
    expect(find.byType(NavigationDestination), findsNWidgets(5));

    await tester.tap(find.text('Bus'));
    await tester.pumpAndSettle();
    expect(find.text('Bus check-in is delivered by a later feature story. This shell '
        'provides the route and tenant/offline-aware scaffolding it will '
        'run inside.'), findsOneWidget);
  });

  testWidgets('shows the offline banner when connectivity drops (AC04)', (tester) async {
    final database = FakeAppDatabase();
    await database.open();
    await database.saveTenantContext(syntheticTenantContext());
    final connectivity = FakeConnectivitySignal(initial: ConnectivityState.online);

    final session = SessionController(database: database, connectivity: connectivity);
    await session.bootstrap();

    await tester.pumpWidget(BiologApp(session: session));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('offline-banner')), findsNothing);

    connectivity.set(ConnectivityState.offline);
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('offline-banner')), findsOneWidget);
  });
}
