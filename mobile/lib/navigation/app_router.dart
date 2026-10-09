// AC03/AC04: routing enforces the explicit-tenant rule (no screen other than
// tenant-select is reachable without a bound tenant) and exposes exactly the
// configured navigation surface (enrolment, verify, scan, bus, offline).
library;

import 'package:go_router/go_router.dart';

import '../core/session/session_controller.dart';
import '../features/bus/bus_screen.dart';
import '../features/enrolment/enrolment_screen.dart';
import '../features/home/app_shell_screen.dart';
import '../features/offline/offline_screen.dart';
import '../features/scan/scan_screen.dart';
import '../features/tenant_select/tenant_select_screen.dart';
import '../features/verify/verify_screen.dart';
import 'nav_items.dart';

const String tenantSelectRoute = '/tenant-select';

GoRouter buildAppRouter(SessionController session) {
  return GoRouter(
    refreshListenable: session,
    initialLocation: tenantSelectRoute,
    redirect: (context, state) {
      if (!session.ready) return null;
      final atTenantSelect = state.matchedLocation == tenantSelectRoute;
      if (!session.hasBoundTenant && !atTenantSelect) return tenantSelectRoute;
      if (session.hasBoundTenant && atTenantSelect) {
        return configuredNavItems.first.route;
      }
      return null;
    },
    routes: [
      GoRoute(
        path: tenantSelectRoute,
        builder: (context, state) => const TenantSelectScreen(),
      ),
      ShellRoute(
        builder: (context, state, child) {
          return AppShellScreen(currentRoute: state.matchedLocation, child: child);
        },
        routes: [
          GoRoute(
            path: '/enrolment',
            builder: (context, state) => const EnrolmentScreen(),
          ),
          GoRoute(
            path: '/verify',
            builder: (context, state) => const VerifyScreen(),
          ),
          GoRoute(
            path: '/scan',
            builder: (context, state) => const ScanScreen(),
          ),
          GoRoute(
            path: '/bus',
            builder: (context, state) => const BusScreen(),
          ),
          GoRoute(
            path: '/offline',
            builder: (context, state) => const OfflineScreen(),
          ),
        ],
      ),
    ],
  );
}
