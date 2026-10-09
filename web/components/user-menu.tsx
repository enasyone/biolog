'use client';

import { useState } from 'react';
import { useSession } from '../context/session-context';

function initialsFor(name: string): string {
  const letters = name
    .split(/\s+/)
    .filter((part) => part && part !== '(SYNTHETIC)')
    .map((part) => part[0])
    .filter(Boolean);
  return letters.slice(0, 2).join('').toUpperCase();
}

/**
 * User menu (BIO-UX-002-AC01). "Sign out" is a real primary action: it flips
 * the demo session to a visible signed-out state (no live auth service
 * exists yet), rather than doing nothing — the approved SIMULATION behaviour.
 */
export function UserMenu() {
  const session = useSession();
  const [open, setOpen] = useState(false);

  if (session.signedOut) {
    return (
      <div className="biolog-user-menu">
        <span className="biolog-signed-out-label">Signed out (SIMULATION)</span>
        <button type="button" className="biolog-user-menu-action" onClick={() => session.signBackIn()}>
          Sign back in
        </button>
      </div>
    );
  }

  return (
    <div className="biolog-user-menu">
      <button
        type="button"
        className="biolog-user-avatar"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        {initialsFor(session.userName)}
        <span className="biolog-visually-hidden">
          User menu for {session.userName}, {session.role.name}
        </span>
      </button>
      {open && (
        <div role="menu" className="biolog-user-menu-panel">
          <p className="biolog-user-menu-name">{session.userName}</p>
          <p className="biolog-user-menu-role">{session.role.name}</p>
          <button
            type="button"
            role="menuitem"
            className="biolog-user-menu-action"
            onClick={() => {
              setOpen(false);
              session.signOut();
            }}
          >
            Sign out
          </button>
        </div>
      )}
    </div>
  );
}
