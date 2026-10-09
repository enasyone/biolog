// BIO-MOB-004-AC01/AC02/AC03/AC04/AC05: the real card/NFC/QR/face
// verification flow, replacing the placeholder shell BIO-MOB-001 shipped
// ("Identity verification is delivered by later feature stories"). AC05:
// this screen IS the mapped surface (`governance/ui-integration-map.json`:
// `surface: "mobile"`, `routes: ["Flutter application"]`, `port:
// "MobileService"`) - no web page to bridge, the identical shape BIO-MOB-005's
// own AC05 already established.
//
// AC01: the result panel renders only `VerificationOutcome`'s own fields
// (decision/reason/assurance level/occurred-at) - that type structurally
// carries no raw biometric evidence at all (see
// `core/verification/verification_outcome.dart`'s own header), so there is
// nothing internal left to accidentally render.
//
// AC02: the app-wide offline banner (`app_shell_screen.dart`) already shows
// connectivity state above every screen; this screen additionally labels
// which path (`VerificationSource.online`/`offlineCache`) actually produced
// the displayed result, and flags a stale cached decision explicitly.
//
// AC03: "Override" (a Supervisor-role-only action - see
// `core/verification/verification_service.dart`'s sibling
// `VerificationSyncClient`) is shown only once this device's own role is
// known AND permits it - never a disabled/dead button standing in for a
// permission this device does not have.
//
// No NFC/QR/card-reader/camera hardware integration exists anywhere in this
// app (verified: no `nfc_manager`/`mobile_scanner`/`camera` pubspec
// dependency) - the credential id is entered directly, standing in for a
// real scan/read, the same disclosed-scope-boundary category BIO-MOB-005's
// own photo/biometric capture toggles already establish for missing
// hardware in this environment.
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/access/access_cache_service.dart';
import '../../core/session/session_controller.dart';
import '../../core/verification/verification_outcome.dart';
import '../../core/verification/verification_service.dart';
import '../../core/verification/verification_sync_client.dart';

const List<String> _credentialClasses = ['card', 'nfc', 'qr', 'face'];

class VerifyScreen extends StatefulWidget {
  const VerifyScreen({super.key});

  @override
  State<VerifyScreen> createState() => _VerifyScreenState();
}

class _VerifyScreenState extends State<VerifyScreen> {
  late final SessionController _session;
  late final VerificationService _service;
  late final VerificationSyncClient _syncClient;

  final TextEditingController _personIdController = TextEditingController();
  final TextEditingController _locationIdController = TextEditingController(text: 'main-gate');
  final TextEditingController _policyKeyController = TextEditingController(text: 'default');
  String _credentialClass = _credentialClasses.first;

  /// `null` until the first (best-effort) read completes - every
  /// role-gated action stays hidden until then, never assumed permitted.
  String? _deviceRole;
  bool _loading = true;
  VerificationOutcome? _lastOutcome;
  String? _statusMessage;
  List<VerificationOutcome> _history = const [];

  @override
  void initState() {
    super.initState();
    _session = context.read<SessionController>();
    _syncClient = VerificationSyncClient(baseUrl: defaultVerificationBaseUrl);
    _service = VerificationService(
      database: _session.database,
      accessCacheService: AccessCacheService(_session.database),
      syncClient: _syncClient,
    );
    _loadDeviceRole();
  }

  @override
  void dispose() {
    _personIdController.dispose();
    _locationIdController.dispose();
    _policyKeyController.dispose();
    super.dispose();
  }

  Future<void> _loadDeviceRole() async {
    try {
      final role = await _syncClient.readDeviceRole(_session.database);
      if (!mounted) return;
      setState(() {
        _deviceRole = role;
        _loading = false;
      });
    } catch (_) {
      // Disclosed scope boundary, mirrors BIO-MOB-003/BIO-ACC-003/BIO-MOB-005
      // identically: no device-enrolment/pairing story exists yet, so every
      // real install is unprovisioned today. Role-gated actions simply stay
      // hidden (never assumed permitted) rather than the screen crashing.
      if (!mounted) return;
      setState(() => _loading = false);
    }
  }

  Future<void> _verify() async {
    final personId = _personIdController.text.trim();
    if (personId.isEmpty) {
      setState(() => _statusMessage = 'Enter a person id to verify.');
      return;
    }

    try {
      final outcome = await _service.verify(
        personId: personId,
        credentialClass: _credentialClass,
        locationId: _locationIdController.text.trim(),
        policyKey: _policyKeyController.text.trim(),
        connectivity: _session.connectivityState,
      );
      if (!mounted) return;
      setState(() {
        _lastOutcome = outcome;
        _statusMessage = outcome.source == VerificationSource.offlineCache
            ? 'Offline: verified from the locally cached deny-list${outcome.stale ? ' (cache is stale)' : ''}.'
            : 'Verified online.';
      });
    } on VerificationDeviceNotProvisionedException {
      setState(() => _statusMessage = 'Offline: no connected verification service available yet (device not provisioned).');
    } on VerificationHttpException catch (error) {
      setState(() => _statusMessage = 'Verification request failed (${error.statusCode}).');
    }
  }

  Future<void> _override() async {
    final outcome = _lastOutcome;
    if (outcome == null) return;

    final reasonCode = await _promptForReasonCode();
    if (reasonCode == null || reasonCode.isEmpty) return;

    try {
      await _syncClient.overrideDecision(
        _session.database,
        decisionEventId: outcome.eventId,
        personId: outcome.personId,
        reasonCode: reasonCode,
      );
      if (!mounted) return;
      setState(() => _statusMessage = 'Decision overridden and recorded.');
    } on VerificationHttpException catch (error) {
      setState(() => _statusMessage = 'Override failed (${error.statusCode}).');
    }
  }

  Future<String?> _promptForReasonCode() {
    final controller = TextEditingController();
    return showDialog<String>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Override reason'),
        content: TextField(
          key: const Key('verify-override-reason'),
          controller: controller,
          decoration: const InputDecoration(labelText: 'Reason code (required)'),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(dialogContext).pop(), child: const Text('Cancel')),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(controller.text.trim()),
            child: const Text('Confirm override'),
          ),
        ],
      ),
    );
  }

  Future<void> _viewHistory() async {
    final personId = _personIdController.text.trim();
    if (personId.isEmpty) {
      setState(() => _statusMessage = 'Enter a person id to view history.');
      return;
    }

    try {
      final history = await _syncClient.fetchHistory(_session.database, personId: personId);
      if (!mounted) return;
      setState(() => _history = history);
    } on VerificationHttpException catch (error) {
      setState(() => _statusMessage = 'History request failed (${error.statusCode}).');
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }

    final canReadHistory = _deviceRole == 'OFFICER' || _deviceRole == 'SUPERVISOR';
    final canOverride = _deviceRole == 'SUPERVISOR' &&
        _lastOutcome != null &&
        _lastOutcome!.decision == VerificationDecision.deny &&
        _lastOutcome!.source == VerificationSource.online;

    return Scaffold(
      appBar: AppBar(title: const Text('Verify')),
      body: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          Text('Identity verification', style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 4),
          Text(
            'Online requests are evaluated live by the tenant\'s access policy. '
            'Offline requests fall back to the locally cached deny-list.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 24),
          TextField(
            key: const Key('verify-person-id'),
            controller: _personIdController,
            decoration: const InputDecoration(labelText: 'Person id (per_...)'),
          ),
          const SizedBox(height: 12),
          DropdownButtonFormField<String>(
            key: const Key('verify-credential-class'),
            initialValue: _credentialClass,
            decoration: const InputDecoration(labelText: 'Credential presented'),
            items: [
              for (final value in _credentialClasses) DropdownMenuItem(value: value, child: Text(value.toUpperCase())),
            ],
            onChanged: (value) => setState(() => _credentialClass = value ?? _credentialClass),
          ),
          const SizedBox(height: 12),
          TextField(
            key: const Key('verify-location-id'),
            controller: _locationIdController,
            decoration: const InputDecoration(labelText: 'Location id'),
          ),
          const SizedBox(height: 12),
          TextField(
            key: const Key('verify-policy-key'),
            controller: _policyKeyController,
            decoration: const InputDecoration(labelText: 'Policy key'),
          ),
          const SizedBox(height: 16),
          FilledButton(
            key: const Key('verify-submit'),
            onPressed: _verify,
            child: const Text('Verify'),
          ),
          if (_statusMessage != null) ...[
            const SizedBox(height: 12),
            Text(_statusMessage!, key: const Key('verify-status-message')),
          ],
          if (_lastOutcome != null) ...[
            const Divider(height: 32),
            _OutcomePanel(outcome: _lastOutcome!),
            if (canOverride) ...[
              const SizedBox(height: 12),
              OutlinedButton(
                key: const Key('verify-override'),
                onPressed: _override,
                child: const Text('Override decision (Supervisor)'),
              ),
            ],
          ],
          if (canReadHistory) ...[
            const Divider(height: 32),
            TextButton(
              key: const Key('verify-view-history'),
              onPressed: _viewHistory,
              child: const Text('View verification history for this person'),
            ),
            for (final entry in _history)
              ListTile(
                key: Key('verify-history-${entry.eventId}'),
                title: Text(entry.decision == VerificationDecision.allow ? 'ALLOW' : 'DENY'),
                subtitle: Text('${entry.reasonCode} — ${entry.occurredAt}'),
              ),
          ],
        ],
      ),
    );
  }
}

class _OutcomePanel extends StatelessWidget {
  const _OutcomePanel({required this.outcome});

  final VerificationOutcome outcome;

  @override
  Widget build(BuildContext context) {
    final allowed = outcome.decision == VerificationDecision.allow;
    return Card(
      color: allowed ? Colors.green.shade50 : Colors.red.shade50,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              allowed ? 'ALLOW' : 'DENY',
              key: const Key('verify-decision'),
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            const SizedBox(height: 4),
            Text('Reason: ${outcome.reasonCode}'),
            if (outcome.presentedAssuranceLevel != null) Text('Assurance level: ${outcome.presentedAssuranceLevel}'),
            Text('Source: ${outcome.source == VerificationSource.online ? 'Live decision' : 'Offline cache'}'),
            if (outcome.source == VerificationSource.offlineCache && outcome.stale)
              const Text('Cache is stale - connect to sync recent revocations.'),
            Text('Occurred at: ${outcome.occurredAt}'),
          ],
        ),
      ),
    );
  }
}
