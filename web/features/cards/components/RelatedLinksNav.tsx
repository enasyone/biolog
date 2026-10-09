import Link from 'next/link';

export interface RelatedLink {
  label: string;
  href: string;
}

/**
 * Replicates `PlaceholderPage`'s own `nav[aria-label="${title} related
 * pages"]` landmark exactly (BIO-UX-002-AC04's existing
 * `bio-ux-002-shell.spec.ts` "related-links from a placeholder page navigate
 * to a real sub-route" test asserts this landmark and link text against
 * `/cards` specifically). Replacing `/cards`'s `PlaceholderPage` with this
 * story's own real registry must not silently drop that navigation path —
 * this component keeps it, styled consistently with the rest of this page:
 * `.biolog-placeholder-related` (TD-UX-FIDELITY-FIXES-BATCH1 — previously
 * unstyled, bare underlined text, unlike every other related-links nav in
 * the app using this exact same markup shape).
 */
export function RelatedLinksNav({ title, links }: { title: string; links: readonly RelatedLink[] }) {
  if (links.length === 0) return null;
  return (
    <nav aria-label={`${title} related pages`} className="biolog-placeholder-related">
      <ul>
        {links.map((link) => (
          <li key={link.href}>
            <Link href={link.href}>{link.label}</Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
