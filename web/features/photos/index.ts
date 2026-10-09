import { resolvePort } from '../../lib/feature-mode';
import { photosMockPort } from './mock';
import { photosLivePort } from './live';
import type { PhotosPort } from './ports';

export type { PhotosPort, PhotoQualityCapture, PhotoQualityCaptureStatus, QualityVerdict } from './ports';

/**
 * The Photos module's resolved port (BIO-UX-005-AC01, AC04). Pages/
 * components import this, never `./mock` or `./live` directly — switching
 * `NEXT_PUBLIC_FEATURE_MODE_PHOTOSERVICE` from SIMULATION to LIVE changes what
 * this resolves to without any importer changing.
 */
export const photosPort: PhotosPort = resolvePort(photosMockPort, photosLivePort);
