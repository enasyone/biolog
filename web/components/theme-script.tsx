const THEME_STORAGE_KEY = 'biolog-theme';

/**
 * Sets `data-theme` on `<html>` before React hydrates (BIO-FND-007-AC01).
 *
 * Runs as an inline, synchronous script so the correct theme applies on the
 * very first paint — waiting for a React effect would flash the wrong theme.
 * Preference order: a saved choice, then the OS `prefers-color-scheme`.
 */
export function ThemeScript() {
  const code = `(function(){try{var s=localStorage.getItem('${THEME_STORAGE_KEY}');var t=s==='light'||s==='dark'?s:(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light');document.documentElement.setAttribute('data-theme',t);}catch(e){}})();`;
  // eslint-disable-next-line react/no-danger
  return <script dangerouslySetInnerHTML={{ __html: code }} />;
}

export { THEME_STORAGE_KEY };
