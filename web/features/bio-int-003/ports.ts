import type { ModulePort, PrimaryActionResult } from '../../lib/module-port';

/**
 * BIO-INT-003's own dedicated connection: tenant-scoped API service-account lifecycle
 * (create/list/rotate-secret/revoke, AC01/AC04), backing a new "API Credentials" panel on
 * `/administration/integrations` mounted ALONGSIDE (not replacing) BIO-INT-002's own
 * `ImportConsole` - same precedent `web/features/bio-rpt-003/ports.ts` already documents for
 * its own two new `ReportingService` ports next to BIO-RPT-001/002's existing ones. The OAuth2
 * client_credentials grant and per-route rate limiting (AC02/AC03) are this story's own real
 * backend behaviour but have no UI surface of their own - route-manifest.ts names no "get a
 * token"/"test rate limit" action for either mapped route, so there is nothing for the web
 * layer to wire; they are exercised directly against `/api/v1/oauth/token` and
 * `/api/v1/integrations/service-accounts/whoami` (see docs/features/bio-int-003/README.md and
 * tests/dotnet/Biolog.IntegrationTests/ApiSecurityEndpointsTests.cs).
 */
export type ServiceAccountLifecycleStatus = 'Active' | 'Revoked';

/** BIO-INT-003-AC02: the closed OAuth2 scope vocabulary - BIO-IDN-006's own 11 role wire
 * strings, duplicated by value (this module's own `KNOWN_SCOPES`, mirroring the backend's
 * `KnownRoles.cs` - see that file's own remarks for why this is a third independent copy). */
export const KNOWN_SCOPES: readonly string[] = [
  'SuperAdmin',
  'TenantAdmin',
  'RegistrarHr',
  'Enrolment',
  'Biometric',
  'CardProduction',
  'Security',
  'Gate',
  'Auditor',
  'Helpdesk',
  'ReadOnly',
];

export interface ServiceAccountSummary {
  id: string;
  clientId: string;
  displayName: string;
  scopes: readonly string[];
  status: ServiceAccountLifecycleStatus;
  version: number;
  createdAt: string;
  secretLastRotatedAt: string;
}

export interface ListServiceAccountsInput {
  tenantId: string;
}

export interface ListServiceAccountsResult extends PrimaryActionResult {
  accounts?: readonly ServiceAccountSummary[];
}

export interface CreateServiceAccountInput {
  tenantId: string;
  displayName: string;
  scopes: readonly string[];
}

export interface CreateServiceAccountResult extends PrimaryActionResult {
  account?: ServiceAccountSummary;
  /** Shown to the caller exactly once (BIO-INT-003-AC04) - never retrievable again. */
  secret?: string;
}

export interface ServiceAccountIdInput {
  tenantId: string;
  id: string;
  expectedVersion: number;
}

export interface RotateServiceAccountSecretResult extends PrimaryActionResult {
  account?: ServiceAccountSummary;
  secret?: string;
}

export interface RevokeServiceAccountResult extends PrimaryActionResult {
  account?: ServiceAccountSummary;
}

export interface ListServiceAccountsPort extends ModulePort {
  primaryAction(input?: ListServiceAccountsInput): Promise<ListServiceAccountsResult>;
}

export interface CreateServiceAccountPort extends ModulePort {
  primaryAction(input?: CreateServiceAccountInput): Promise<CreateServiceAccountResult>;
}

export interface RotateServiceAccountSecretPort extends ModulePort {
  primaryAction(input?: ServiceAccountIdInput): Promise<RotateServiceAccountSecretResult>;
}

export interface RevokeServiceAccountPort extends ModulePort {
  primaryAction(input?: ServiceAccountIdInput): Promise<RevokeServiceAccountResult>;
}
