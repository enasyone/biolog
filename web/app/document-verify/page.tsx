import { DocumentVerifyApp } from '../../features/documents/DocumentVerifyApp';

/**
 * BIO-UX-009: the real, interactive Document Verify journey, replacing the
 * BIO-UX-002 `PlaceholderPage` shell this route rendered until now. See
 * `web/features/documents/DocumentVerifyApp.tsx` for the actual page.
 */
export default function DocumentVerifyPage() {
  return <DocumentVerifyApp />;
}
