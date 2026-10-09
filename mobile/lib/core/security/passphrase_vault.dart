// AC02: the SQLCipher passphrase is generated once per install, at random,
// and never hardcoded or bundled (AC05). It is stored only in the
// Keystore-backed secure store, never in the encrypted database itself
// (that would defeat the point) and never logged.
library;

import 'dart:convert';
import 'dart:math';

import 'secure_key_value_store.dart';

const int passphraseBytes = 32;

/// Generates a cryptographically random passphrase suitable for SQLCipher.
/// Pure function, independent of storage, so its randomness/shape can be
/// tested directly.
String generatePassphrase(Random random) {
  final bytes = List<int>.generate(passphraseBytes, (_) => random.nextInt(256));
  return base64UrlEncode(bytes);
}

class PassphraseVault {
  PassphraseVault({
    required SecureKeyValueStore store,
    Random? random,
    String storageKey = 'biolog.mobile.sqlcipher_passphrase_v1',
  })  : _store = store,
        _random = random ?? Random.secure(),
        _storageKey = storageKey;

  final SecureKeyValueStore _store;
  final Random _random;
  final String _storageKey;

  /// Returns the existing passphrase if one was already generated for this
  /// install, otherwise mints and persists a new one. Idempotent: repeated
  /// calls against the same store always return the same value.
  Future<String> loadOrCreate() async {
    final existing = await _store.read(_storageKey);
    if (existing != null && existing.isNotEmpty) return existing;
    final generated = generatePassphrase(_random);
    await _store.write(_storageKey, generated);
    return generated;
  }
}
