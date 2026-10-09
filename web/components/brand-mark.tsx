/**
 * BIOLOG wordmark (BIO-FND-007-AC02). Rendered as an `<img>` so its fixed
 * internal colours are opaque to the page's light/dark theme and to
 * text-contrast checks — it is announced to assistive tech via `alt`, not
 * read as text.
 */
export function BrandMark() {
  return (
    <span className="biolog-brand-plate">
      <img
        src="/brand/biolog-wordmark-light.svg"
        alt="BIOLOG — Biometric Technology"
        width={165}
        height={46}
      />
    </span>
  );
}
