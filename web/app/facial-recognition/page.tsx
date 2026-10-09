import { FaceJourneyApp } from '../../features/bio-ux-008/FaceJourneyApp';

/**
 * BIO-UX-008: the real `/facial-recognition` route (previously a
 * `PlaceholderPage` shell since BIO-UX-002). Capture, quality, 1:1
 * verification, 1:N identification and the liveness placeholder are all
 * interactive here (AC02).
 */
export default function FacialRecognitionPage() {
  return (
    <div className="biolog-placeholder-page">
      <h1>Facial Recognition</h1>
      <p className="biolog-placeholder-description">Face capture, quality, 1:1 verification and 1:N identification.</p>
      <FaceJourneyApp />
    </div>
  );
}
