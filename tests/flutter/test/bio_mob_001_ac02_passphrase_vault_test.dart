// BIO-MOB-001-AC02: sensitive local data is encrypted. This suite covers the
// passphrase that keys the on-device SQLCipher store: it must be random per
// install, never a bundled/production default, persisted only in the
// injected secure store, and stable across app restarts.
import 'dart:math';

import 'package:biolog_mobile/core/security/passphrase_vault.dart';
import 'package:biolog_mobile/core/security/secure_key_value_store.dart';
import 'package:flutter_test/flutter_test.dart';

class _InMemorySecureStore implements SecureKeyValueStore {
  final Map<String, String> _values = {};

  @override
  Future<String?> read(String key) async => _values[key];

  @override
  Future<void> write(String key, String value) async {
    _values[key] = value;
  }
}

void main() {
  test('generatePassphrase never returns a bundled/hardcoded value', () {
    final a = generatePassphrase(Random.secure());
    final b = generatePassphrase(Random.secure());
    expect(a, isNot(equals(b)));
    expect(a.length, greaterThanOrEqualTo(40));
  });

  test('loadOrCreate mints a passphrase on first use', () async {
    final vault = PassphraseVault(store: _InMemorySecureStore());
    final passphrase = await vault.loadOrCreate();
    expect(passphrase, isNotEmpty);
  });

  test('loadOrCreate is idempotent across app restarts (same store)', () async {
    final store = _InMemorySecureStore();
    final first = await PassphraseVault(store: store).loadOrCreate();
    // A fresh vault instance simulates a cold app restart re-reading the
    // same underlying Keystore-backed store.
    final second = await PassphraseVault(store: store).loadOrCreate();
    expect(second, equals(first));
  });

  test('two installs (independent stores) never share a passphrase', () async {
    final first = await PassphraseVault(store: _InMemorySecureStore()).loadOrCreate();
    final second = await PassphraseVault(store: _InMemorySecureStore()).loadOrCreate();
    expect(first, isNot(equals(second)));
  });
}
