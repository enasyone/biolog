import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import './globals.css';
import { ThemeScript } from '../components/theme-script';
import { AppShell } from '../components/app-shell';
import { SessionProvider } from '../context/session-context';
import { FeatureModePanel } from '../dev-tools/feature-mode-panel';
import { HumanTestConsole } from '../dev-tools/human-test-console';

export const metadata: Metadata = {
  title: 'BIOLOG',
  description:
    'BIOLOG identity, biometric, credential, card-production, attendance, access, visitor and transport platform.',
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <head>
        <ThemeScript />
      </head>
      <body>
        <SessionProvider>
          <AppShell>{children}</AppShell>
          <HumanTestConsole />
        </SessionProvider>
        <FeatureModePanel />
      </body>
    </html>
  );
}
