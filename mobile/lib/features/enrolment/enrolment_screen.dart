// BIO-MOB-005-AC01/AC02/AC03/AC05: the real limited offline enrolment
// workflow, replacing the placeholder shell BIO-MOB-001 shipped ("Enrolment
// capture ... is delivered by later feature stories"). AC05: this screen
// IS the mapped surface (`governance/ui-integration-map.json`:
// `surface: "mobile"`, `routes: ["Flutter application"]`, `port:
// "MobileService"`) — no web page to bridge, unlike BIO-ACC-003-AC06.
//
// Fields and biometric/photo toggles shown are gated by the tenant's own
// offline capture policy (AC01, `EnrolmentService.currentCapturePolicy`) —
// never a fixed set. Saving queues the package in the same SQLCipher-
// encrypted local database every other offline queue in this app uses
// (AC02) and always durably records the capture regardless of connectivity
// (AC03's own local half — the actual central duplicate scan happens once
// this reaches the server).
//
// No camera or fingerprint-scanner hardware integration exists anywhere in
// this app (verified during design: no `camera`/`image_picker` package is a
// mobile dependency; BIO-PHO-001/BIO-BIO-002 backend stories are
// explicitly session/metadata-only, no capture UX). Photo/biometric capture
// is therefore represented by a policy-gated toggle that attaches a fixed,
// clearly-labelled SYNTHETIC placeholder payload when enabled — the same
// disclosed-scope-boundary category BIO-BIO-001's own
// `SyntheticFingerprintProvider`/`SyntheticFaceProvider` already establish
// for hardware this environment does not have, not a fabricated capture.
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/enrolment/enrolment_capture_policy.dart';
import '../../core/enrolment/enrolment_package.dart';
import '../../core/enrolment/enrolment_service.dart';
import '../../core/enrolment/enrolment_sync_client.dart';
import '../../core/session/session_controller.dart';

/// A fixed, clearly-labelled synthetic payload — never real captured
/// bytes — standing in for a photo/biometric this device has no hardware
/// to actually capture (see this file's own header).
const String _syntheticCapturePlaceholder = 'SYNTHETIC_PLACEHOLDER_NO_HARDWARE_CAPTURE_AVAILABLE';

const Map<String, String> _fieldLabels = {
  fieldFirstName: 'First name',
  fieldMiddleName: 'Middle name',
  fieldLastName: 'Last name',
  fieldBornOn: 'Date of birth (YYYY-MM-DD)',
  fieldInstitutionalIdentifier: 'Institutional identifier',
};

const Map<String, String> _modalityLabels = {
  modalityFingerprint: 'Fingerprint',
  modalityFace: 'Face',
};

class EnrolmentScreen extends StatefulWidget {
  const EnrolmentScreen({super.key});

  @override
  State<EnrolmentScreen> createState() => _EnrolmentScreenState();
}

class _EnrolmentScreenState extends State<EnrolmentScreen> {
  late final SessionController _session;
  late final EnrolmentService _service;
  late final EnrolmentSyncClient _syncClient;

  EnrolmentCapturePolicy _policy = EnrolmentCapturePolicy.textOnlyDefault;
  List<EnrolmentPackage> _packages = const [];
  final Map<String, TextEditingController> _fieldControllers = {};
  bool _photoToggle = false;
  final Map<String, bool> _biometricToggles = {};
  bool _loading = true;
  String? _statusMessage;

  @override
  void initState() {
    super.initState();
    _session = context.read<SessionController>();
    _service = EnrolmentService(_session.database);
    _syncClient = EnrolmentSyncClient(baseUrl: defaultEnrolmentBaseUrl);
    _load();
  }

  @override
  void dispose() {
    for (final controller in _fieldControllers.values) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<void> _load() async {
    final policy = await _service.currentCapturePolicy();
    final packages = await _session.database.allEnrolmentPackages();
    if (!mounted) return;
    setState(() {
      _policy = policy;
      _packages = packages;
      for (final field in _policy.allowedFields) {
        _fieldControllers.putIfAbsent(field, () => TextEditingController());
      }
      _loading = false;
    });
  }

  TextEditingController _controllerFor(String field) =>
      _fieldControllers.putIfAbsent(field, () => TextEditingController());

  Future<void> _refreshCapturePolicy() async {
    try {
      final policy = await _syncClient.pullCapturePolicy(_session.database);
      if (!mounted) return;
      setState(() {
        _policy = policy;
        for (final field in _policy.allowedFields) {
          _fieldControllers.putIfAbsent(field, () => TextEditingController());
        }
        _statusMessage = 'Capture policy refreshed (v${policy.version}).';
      });
    } on EnrolmentDeviceNotProvisionedException {
      // Disclosed scope boundary (mirrors BIO-MOB-003/BIO-ACC-003
      // identically): no device-enrolment/pairing story exists yet, so
      // every real install is unprovisioned today. A visible, honest
      // outcome — not a dead button, not a crash.
      setState(() => _statusMessage = 'Offline: no connected policy available yet (device not provisioned). Using the locally cached policy.');
    } catch (_) {
      setState(() => _statusMessage = 'Offline: could not reach the capture-policy service. Using the locally cached policy.');
    }
  }

  Future<void> _saveEnrolment() async {
    final fieldValues = <String, String>{};
    for (final field in _policy.allowedFields) {
      final value = _controllerFor(field).text.trim();
      if (value.isNotEmpty) {
        fieldValues[field] = value;
      }
    }

    final biometricPayloads = <String, String>{};
    for (final modality in _policy.allowedBiometricModalities) {
      if (_biometricToggles[modality] ?? false) {
        biometricPayloads[modality] = _syntheticCapturePlaceholder;
      }
    }

    try {
      await _service.capture(
        fieldValues: fieldValues,
        photoContentBase64: (_policy.allowPhotoCapture && _photoToggle) ? _syntheticCapturePlaceholder : null,
        biometricPayloadsBase64: biometricPayloads,
      );
      final packages = await _session.database.allEnrolmentPackages();
      if (!mounted) return;
      setState(() {
        _packages = packages;
        _statusMessage = 'Enrolment captured and queued locally, encrypted at rest. It will sync once connectivity returns.';
        for (final controller in _fieldControllers.values) {
          controller.clear();
        }
        _photoToggle = false;
        _biometricToggles.clear();
      });
    } on EnrolmentPolicyViolationException catch (error) {
      setState(() => _statusMessage = error.message);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }

    return Scaffold(
      appBar: AppBar(
        title: const Text('Enrolment'),
        actions: [
          IconButton(
            key: const Key('enrolment-refresh-policy'),
            icon: const Icon(Icons.sync),
            tooltip: 'Refresh capture policy',
            onPressed: _refreshCapturePolicy,
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(24),
        children: [
          Text(
            'Limited offline enrolment',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 4),
          Text(
            'Only fields and biometrics this tenant has permitted for offline '
            'capture are shown below (capture policy v${_policy.version}).',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 24),
          for (final field in _policy.allowedFields)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: TextField(
                key: Key('enrolment-field-$field'),
                controller: _controllerFor(field),
                decoration: InputDecoration(labelText: _fieldLabels[field] ?? field),
              ),
            ),
          if (_policy.allowPhotoCapture)
            CheckboxListTile(
              key: const Key('enrolment-photo-toggle'),
              title: const Text('Capture photo (synthetic placeholder — no camera hardware in this environment)'),
              value: _photoToggle,
              onChanged: (value) => setState(() => _photoToggle = value ?? false),
            ),
          for (final modality in _policy.allowedBiometricModalities)
            CheckboxListTile(
              key: Key('enrolment-biometric-$modality'),
              title: Text('Capture ${_modalityLabels[modality] ?? modality} (synthetic placeholder — no scanner hardware in this environment)'),
              value: _biometricToggles[modality] ?? false,
              onChanged: (value) => setState(() => _biometricToggles[modality] = value ?? false),
            ),
          const SizedBox(height: 16),
          FilledButton(
            key: const Key('enrolment-save'),
            onPressed: _saveEnrolment,
            child: const Text('Save enrolment (queue offline)'),
          ),
          if (_statusMessage != null) ...[
            const SizedBox(height: 12),
            Text(_statusMessage!, key: const Key('enrolment-status-message')),
          ],
          const Divider(height: 32),
          Text('Queued enrolments (${_packages.length})', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          for (final package in _packages)
            ListTile(
              key: Key('enrolment-package-${package.packageId}'),
              title: Text(package.fieldValues.values.join(' ').trim().isEmpty
                  ? '(no text fields captured)'
                  : package.fieldValues.values.join(' ')),
              subtitle: Text('Status: ${package.status.toWire()}'),
              leading: const Icon(Icons.person_add_alt_1),
            ),
        ],
      ),
    );
  }
}
