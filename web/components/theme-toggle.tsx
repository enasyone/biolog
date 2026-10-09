'use client';

import { useEffect, useState } from 'react';
import { THEME_STORAGE_KEY } from './theme-script';
import type { ThemeName } from '../lib/tokens';

function readCurrentTheme(): ThemeName {
  if (typeof document === 'undefined') return 'light';
  return document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
}

/** Light/dark toggle (BIO-FND-007-AC01). Persists the choice for next visit. */
export function ThemeToggle() {
  const [theme, setTheme] = useState<ThemeName>('light');

  useEffect(() => {
    setTheme(readCurrentTheme());
  }, []);

  function toggle() {
    const next: ThemeName = theme === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', next);
    try {
      localStorage.setItem(THEME_STORAGE_KEY, next);
    } catch {
      // Storage may be unavailable (private mode/quota); theme still applies for this view.
    }
    setTheme(next);
  }

  return (
    <button
      type="button"
      className="biolog-theme-toggle"
      onClick={toggle}
      aria-pressed={theme === 'dark'}
    >
      <span aria-hidden="true">{theme === 'dark' ? '☀️' : '🌙'}</span>
      <span className="biolog-visually-hidden">
        Switch to {theme === 'dark' ? 'light' : 'dark'} theme
      </span>
    </button>
  );
}
