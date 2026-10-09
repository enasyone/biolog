// Narrow interface over secure, at-rest key/value storage (AC02). Kept
// separate from `flutter_secure_storage` directly so the passphrase
// derivation logic that depends on it (`PassphraseVault`) is unit-testable
// on the host without a platform channel.
library;

abstract class SecureKeyValueStore {
  Future<String?> read(String key);

  Future<void> write(String key, String value);
}
