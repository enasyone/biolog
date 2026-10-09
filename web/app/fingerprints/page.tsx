import { FingerprintJourneyApp } from '../../features/bio-ux-008/FingerprintJourneyApp';

/**
 * BIO-UX-008: the real `/fingerprints` route (previously a `PlaceholderPage`
 * shell since BIO-UX-002). Tenprint, single-finger, quality, exception and
 * 1:1 verification are all interactive here (AC01).
 */
export default function FingerprintsPage() {
  return (
    <div className="biolog-placeholder-page">
      <h1>Fingerprints</h1>
      <p className="biolog-placeholder-description">Tenprint and single-finger capture, quality and verification.</p>
      <FingerprintJourneyApp />
    </div>
  );
}
