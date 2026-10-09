// Real SecureKeyValueStore backed by the Android Keystore via
// `flutter_secure_storage` (EncryptedSharedPreferences).
library;

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'secure_key_value_store.dart';

class SecureStorageKeyValueStore implements SecureKeyValueStore {
  // Default AndroidOptions() already wraps values with Keystore-backed
  // AES-GCM (RSA-OAEP key wrapping) in flutter_secure_storage 11.x — no
  // legacy `encryptedSharedPreferences` flag is needed or exposed.
  SecureStorageKeyValueStore({FlutterSecureStorage? storage})
      : _storage = storage ?? const FlutterSecureStorage(aOptions: AndroidOptions());

  final FlutterSecureStorage _storage;

  @override
  Future<String?> read(String key) => _storage.read(key: key);

  @override
  Future<void> write(String key, String value) => _storage.write(key: key, value: value);
}
