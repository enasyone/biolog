import { CardsRegistryApp } from '../../features/cards/CardsRegistryApp';

/**
 * BIO-UX-007-AC01: the real Cards registry, replacing the `PlaceholderPage`
 * shell BIO-UX-002 shipped here (`docs/MANUAL.md`: "the domain story that
 * owns this area replaces the page ... when it ships"). See
 * `docs/features/bio-ux-007/README.md`.
 */
export default function CardsPage() {
  return <CardsRegistryApp />;
}
