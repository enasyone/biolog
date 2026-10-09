// AC03: tenant context is explicit — which also means the app must never
// invent one. BIO-MOB-001-QA-02 (Chioma, acceptance round 2): this screen
// previously asked the operator to type a tenant id and a database binding
// key, then treated the typed values as an authenticated, resolved tenant
// (ADR-0002 Decision 4.1: field shape is not proof of resolution; Decision
// 1.1: a mobile client cannot originate the `principal` channel itself — it
// is a token claim from a real auth boundary, BIO-INT-001, not shipped yet).
//
// There is no form here anymore. This screen is what the app honestly shows
// while no trusted resolution boundary exists: the shell stays unbound
// rather than ship a production resolution shim.
library;

import 'package:flutter/material.dart';

class TenantSelectScreen extends StatelessWidget {
  const TenantSelectScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('BIOLOG')),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.hourglass_top, size: 48),
              const SizedBox(height: 16),
              Text(
                'Tenant resolution is not yet available on this device',
                style: Theme.of(context).textTheme.titleLarge,
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 12),
              const Text(
                'BIOLOG never resolves a tenant from information entered on a '
                'device (ADR-0002): a tenant claim must come from a trusted, '
                'unforgeable resolution boundary. This build ships ahead of '
                'that boundary, so the app intentionally stays unbound until '
                'it exists.',
                textAlign: TextAlign.center,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
