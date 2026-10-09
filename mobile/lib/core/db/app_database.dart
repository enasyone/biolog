// Local secure-store contract for this app (AC02). Deliberately an
// interface: the real implementation (`SqlCipherAppDatabase`) needs a native
// SQLCipher plugin that only runs on a device/emulator, so widget and unit
// tests inject a fake in-memory implementation instead of skipping coverage
// of everything that depends on storage.
library;

import '../access/presentation_event.dart';
import '../access/revocation_entry.dart';
import '../enrolment/enrolment_capture_policy.dart';
import '../enrolment/enrolment_package.dart';
import '../offline/idempotent_enqueue.dart' show IdempotencyConflictError;
import '../offline/offline_mutation.dart';
import '../offline/retry_policy.dart' show RetryPolicy;
import '../tenant/tenant_context.dart';

/// Local encrypted store for tenant context, the device identity and the
/// offline mutation queue. All sensitive local data (AC02) is written
/// through this interface — nothing in the app touches SQLite directly.
abstract class AppDatabase {
  /// Open (creating and migrating if needed) the encrypted store. Must be
  /// called once before any other method.
  Future<void> open();

  Future<void> close();

  /// This installation's own canonical device id (`dev_<uuidv7>`), minted
  /// once on first launch and persisted thereafter.
  Future<String> deviceId();

  /// Returns the persisted tenant context only if it carries verified
  /// resolution provenance (BIO-MOB-001-QA-03) — never merely because it is
  /// schema-shape-valid (ADR-0002 Decision 4.1). A stored record with no
  /// provenance marker (e.g. one written by a now-removed vulnerable local
  /// producer, still present after an app upgrade) is invalidated in place
  /// and this returns `null`, silently and without throwing — that is not a
  /// corrupted record (BIO-MOB-001-QA-01's case, which still throws
  /// [TenantBindingError]), it is an untrusted one.
  Future<TenantContext?> loadTrustedTenantContext();

  /// Persists [context] together with this device's resolution-provenance
  /// marker, so a future read can tell it apart from a legacy,
  /// caller-constructed record (BIO-MOB-001-QA-03).
  ///
  /// **The real ([SqlCipherAppDatabase]) implementation always throws**
  /// (BIO-MOB-001-QA-04): stamping any caller-supplied [TenantContext] as
  /// trusted, unconditionally, is exactly how a forged context reached
  /// storage in the first place — [TenantContext.fromTrustedJson] validates
  /// shape only, so holding a [TenantContext] instance is not itself proof
  /// of resolution. There is no legitimate caller until a real resolution
  /// boundary exists to justify a real implementation (mirrors
  /// `TenantContextMiddleware`'s "unconditionally null" pattern, ADR-0002
  /// Decision 6). Test doubles ([FakeAppDatabase]) may implement this
  /// normally — Decision 6 sanctions exactly that for exercising downstream
  /// logic ahead of the real resolver.
  Future<void> saveTenantContext(TenantContext context);

  Future<void> clearTenantContext();

  /// Clears a persisted tenant-context record that lacks verified resolution
  /// provenance (BIO-MOB-001-QA-03). Distinct from [clearTenantContext] in
  /// caller intent — this is bootstrap self-healing an untrusted legacy
  /// record it refuses to bind, not an operator-initiated sign-out.
  Future<void> invalidateLegacyTenantContext();

  /// Enqueues [mutation]. A reused `idempotency_key` with identical logical
  /// content (same tenant/device/aggregate/base_version/payload) is treated
  /// as a replay: no write happens and the originally-queued mutation is
  /// returned. A reused key with different content throws
  /// [IdempotencyConflictError] — the queue never silently replaces one
  /// mutation's payload with another's by key (BIO-MOB-001-F01).
  Future<OfflineMutation> enqueueMutation(OfflineMutation mutation);

  Future<List<OfflineMutation>> pendingMutations();

  Future<int> countMutationsByState(OfflineSyncState state);

  /// Marks a `pending` mutation as currently being uploaded
  /// (BIO-MOB-002-AC02: `pending` -> `uploading`). Returns whether the
  /// guarded transition actually happened — `false` means [mutationId] was
  /// not `pending` (already claimed by a concurrent caller, or in some other
  /// state entirely) and this call was a no-op. BIO-MOB-003-AC05:
  /// [SyncEngine] uses this to tell "I actually own this upload" apart from
  /// "someone else already does," so a reconnect storm racing two sync
  /// passes over the same due mutation issues at most one upload for it,
  /// not two.
  Future<bool> markUploading(String mutationId);

  /// Records a successful, non-conflicting server acknowledgement for
  /// [mutationId] (BIO-MOB-003-AC02: `uploading` -> `acknowledged`). Applies
  /// identically whether the acknowledgement was a fresh execution or an
  /// idempotent replay of an already-recorded one (ADR-0001 §3.4) — the
  /// caller ([SyncEngine]) does not branch on that distinction, and neither
  /// does this method. Guarded to only apply to a mutation currently
  /// `uploading`, mirroring every other transition in this queue.
  Future<void> markAcknowledged(String mutationId);

  /// Records a failed upload attempt for [mutationId] (BIO-MOB-002-AC03).
  /// Increments its retry count; if the bounded [RetryPolicy] still has
  /// attempts remaining, schedules the next one with exponential backoff and
  /// returns the mutation to `pending`. Once attempts are exhausted, moves it
  /// to the terminal `failed` state instead — "bounded" retry, not infinite.
  Future<void> recordUploadFailure(String mutationId, {DateTime? now});

  /// Records a security-sensitive version conflict for [mutationId]
  /// (BIO-MOB-002-AC04, ADR-0001 §3.5): moves it to `conflict`, out of the
  /// automatic retry cycle entirely, for the human Sync Resolution Queue.
  /// The mutation is never merged and never silently overwritten by a
  /// last-write-wins resolution — it sits in `conflict` until a human acts.
  Future<void> recordConflict(String mutationId);

  /// `pending` mutations currently due for an upload attempt: never
  /// attempted, or their backoff window has elapsed by [now]
  /// (BIO-MOB-002-AC03). Ordered oldest-composed-first.
  Future<List<OfflineMutation>> dueForRetry(DateTime now);

  /// Mutations sitting in `conflict`, awaiting the human Sync Resolution
  /// Queue (BIO-MOB-002-AC04, ADR-0001 §3.5) — never auto-resolved.
  Future<List<OfflineMutation>> conflictedMutations();

  // --- BIO-ACC-003: offline deny-list and cached access decisions ---

  /// Merges [entries] into the locally cached revocation deny-list (AC01:
  /// minimum necessary — only a person id and the instant it was revoked are
  /// ever stored; AC02: revocation deltas are additive, there is no
  /// un-revoke transition to reconcile against — see
  /// `ICrossTenantPersonRevocationLookup`'s own header). Advances the delta
  /// cursor to [cursor] and marks the cache synced as of [syncedAt] — even
  /// when [entries] is empty, since a successful empty poll still proves the
  /// cache is current as of now (AC04's own staleness input).
  Future<void> cacheRevocations(List<RevocationEntry> entries, {String? cursor, required DateTime syncedAt});

  /// True if [personId] is present in the locally cached deny-list (AC03).
  Future<bool> isRevoked(String personId);

  /// The delta cursor to resume from on the next pull, or `null` if the
  /// cache has never synced.
  Future<String?> revocationCacheCursor();

  /// When the revocation cache was last successfully synced, or `null` if
  /// never — AC04's own explicit staleness input
  /// (`stale_cache_policy.dart`).
  Future<DateTime?> revocationCacheLastSyncedAt();

  /// Durably logs [event] (AC03) — must succeed fully offline; this method
  /// makes no network call and requires none to succeed.
  Future<void> recordPresentationEvent(PresentationEvent event);

  /// Presentation events not yet uploaded (AC05), oldest [PresentationEvent.occurredAt] first.
  Future<List<PresentationEvent>> pendingPresentationEvents();

  /// Marks [eventId] as uploaded — idempotent: marking an already-synced or
  /// unknown event is a no-op, never an error (a reconnect-storm retry of
  /// the same acknowledgement must not throw).
  Future<void> markPresentationEventSynced(String eventId);

  // --- BIO-MOB-005: limited offline enrolment package ---

  /// Persists the tenant's offline capture policy locally (AC01) — pulled
  /// from `GET /api/v1/enrolment/capture-policy` whenever connectivity
  /// allows, so the enrolment screen can gate which fields/biometrics it
  /// shows/enables even while fully offline, using the most recently known
  /// policy rather than always falling back to
  /// [EnrolmentCapturePolicy.textOnlyDefault].
  Future<void> cacheCapturePolicy(EnrolmentCapturePolicy policy);

  /// The most recently cached capture policy, or `null` if this device has
  /// never successfully fetched one (its very first launch before any
  /// connectivity) — the caller falls back to
  /// [EnrolmentCapturePolicy.textOnlyDefault] in that case.
  Future<EnrolmentCapturePolicy?> loadCapturePolicy();

  /// Enqueues [package] for later sync (AC02: encrypted at rest by virtue of
  /// living in this same SQLCipher-encrypted database; AC05: uploaded at
  /// most once via [pendingEnrolmentPackages]/[markEnrolmentPackageSynced]).
  Future<void> enqueueEnrolmentPackage(EnrolmentPackage package);

  /// Packages not yet uploaded (status still `submitted` locally — this
  /// device has not yet learned the server's own AC03 outcome), oldest
  /// [EnrolmentPackage.submittedAt] first.
  Future<List<EnrolmentPackage>> pendingEnrolmentPackages();

  /// Records the server's own AC03/AC04 outcome for [packageId] once the
  /// upload succeeds — idempotent: updating an already-synced or unknown
  /// package id is a no-op, never an error (a reconnect-storm retry of the
  /// same acknowledgement must not throw).
  Future<void> markEnrolmentPackageSynced(String packageId, EnrolmentPackageStatus status);

  /// Every enrolment package this device has ever queued, most recent
  /// [EnrolmentPackage.submittedAt] first — the enrolment screen's own
  /// "recently captured" status list.
  Future<List<EnrolmentPackage>> allEnrolmentPackages();
}
