import type {
  CreateServiceAccountPort,
  ListServiceAccountsPort,
  RevokeServiceAccountPort,
  RotateServiceAccountSecretPort,
  ServiceAccountSummary,
} from './ports';

/**
 * Live adapters for BIO-INT-003's real `/api/v1/integrations/service-accounts/*` backend
 * (`BioInt003Module.cs`) - genuinely connected, not a stub. No tenant header is sent; this
 * host resolves tenancy from the request's own Host channel (ADR-0002 Decision 1.3,
 * `TenantContextMiddleware`), the same convention every other live adapter in this epic
 * already follows (see `web/features/integrations/live.ts`).
 */
const ACTOR_HEADERS = { 'X-Actor-Id': 'web-ui', 'X-Actor-Type': 'user' } as const;
const BASE_PATH = '/api/v1/integrations/service-accounts';

interface ServiceAccountWire {
  id: string;
  clientId: string;
  displayName: string;
  scopes: readonly string[];
  status: string;
  version: number;
  createdAt: string;
  secretLastRotatedAt: string;
  secret?: string | null;
}

function fromWire(wire: ServiceAccountWire): ServiceAccountSummary {
  return {
    id: wire.id,
    clientId: wire.clientId,
    displayName: wire.displayName,
    scopes: wire.scopes,
    status: wire.status as ServiceAccountSummary['status'],
    version: wire.version,
    createdAt: wire.createdAt,
    secretLastRotatedAt: wire.secretLastRotatedAt,
  };
}

async function problemMessage(response: Response, fallback: string): Promise<string> {
  try {
    const body = (await response.json()) as { title?: string; detail?: string };
    return body.detail ?? body.title ?? fallback;
  } catch {
    return fallback;
  }
}

export const listServiceAccountsLivePort: ListServiceAccountsPort = {
  module: 'IntegrationService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No tenant was provided.' };
    try {
      const response = await fetch(BASE_PATH, { headers: ACTOR_HEADERS });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not load service accounts: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const wire = (await response.json()) as ServiceAccountWire[];
      return { outcome: 'success', message: 'Service accounts loaded.', accounts: wire.map(fromWire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the API Security service - device or network appears offline.' };
    }
  },
};

export const createServiceAccountLivePort: CreateServiceAccountPort = {
  module: 'IntegrationService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No service account definition was provided.' };
    try {
      const response = await fetch(BASE_PATH, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify({ displayName: input.displayName, scopes: input.scopes }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not create service account: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const wire = (await response.json()) as ServiceAccountWire;
      return { outcome: 'success', message: `Service account '${wire.displayName}' created - copy the secret now, it will not be shown again.`, account: fromWire(wire), secret: wire.secret ?? undefined };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the API Security service - device or network appears offline.' };
    }
  },
};

export const rotateServiceAccountSecretLivePort: RotateServiceAccountSecretPort = {
  module: 'IntegrationService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No service account was provided.' };
    try {
      const response = await fetch(`${BASE_PATH}/${input.id}/rotate-secret`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify({ expectedVersion: input.expectedVersion }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not rotate secret: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const wire = (await response.json()) as ServiceAccountWire;
      return { outcome: 'success', message: 'Secret rotated - copy it now, it will not be shown again.', account: fromWire(wire), secret: wire.secret ?? undefined };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the API Security service - device or network appears offline.' };
    }
  },
};

export const revokeServiceAccountLivePort: RevokeServiceAccountPort = {
  module: 'IntegrationService',
  mode: 'live',
  async primaryAction(input) {
    if (!input) return { outcome: 'failure', message: 'No service account was provided.' };
    try {
      const response = await fetch(`${BASE_PATH}/${input.id}/revoke`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...ACTOR_HEADERS },
        body: JSON.stringify({ expectedVersion: input.expectedVersion }),
      });
      if (!response.ok) {
        return { outcome: 'failure', message: `Could not revoke service account: ${await problemMessage(response, `HTTP ${response.status}`)}` };
      }
      const wire = (await response.json()) as ServiceAccountWire;
      return { outcome: 'success', message: `'${wire.displayName}' revoked.`, account: fromWire(wire) };
    } catch {
      return { outcome: 'offline', message: 'Could not reach the API Security service - device or network appears offline.' };
    }
  },
};
