const GITHUB_API_URL = 'https://api.github.com';
const GITHUB_API_VERSION = '2022-11-28';
const REPOSITORY = 'ThibaultPoujat/Tyrian_Ledger';
const WORKFLOW = 'pages.yml';
const BRANCH = 'develop';

export interface SchedulerEnvironment {
  SCHEDULER_ENABLED?: string;
  GITHUB_APP_ID?: string;
  GITHUB_APP_INSTALLATION_ID?: string;
  GITHUB_APP_PRIVATE_KEY?: string;
}

interface SchedulerDependencies {
  createAppJwt: (appId: string, privateKey: string, nowMs: number) => Promise<string>;
  fetch: typeof fetch;
  now: () => number;
}

interface SafeLogger {
  error: (message: string, details: Record<string, string | number>) => void;
  info: (message: string, details?: Record<string, string | number>) => void;
}

type SchedulerFailureCode = 'configuration' | 'dispatch-request' | 'token-request' | 'token-response' | 'unexpected';

export class SchedulerFailure extends Error {
  public constructor(
    public readonly code: SchedulerFailureCode,
    public readonly status?: number,
  ) {
    super(code);
  }
}

interface SchedulerConfiguration {
  appId: string;
  installationId: string;
  privateKey: string;
}

function base64UrlEncode(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

function jsonBase64Url(value: object): string {
  return base64UrlEncode(new TextEncoder().encode(JSON.stringify(value)));
}

function parsePkcs8PrivateKey(value: string): ArrayBuffer {
  const match = /^-----BEGIN PRIVATE KEY-----\s+([\s\S]+?)\s+-----END PRIVATE KEY-----$/.exec(value.trim());
  if (match === null) throw new SchedulerFailure('configuration');

  try {
    const binary = atob(match[1].replaceAll(/\s/g, ''));
    const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
    return bytes.buffer;
  } catch {
    throw new SchedulerFailure('configuration');
  }
}

export async function createGitHubAppJwt(appId: string, privateKey: string, nowMs: number): Promise<string> {
  const issuedAt = Math.floor(nowMs / 1_000) - 30;
  const header = jsonBase64Url({ alg: 'RS256', typ: 'JWT' });
  const payload = jsonBase64Url({ exp: issuedAt + 9 * 60, iat: issuedAt, iss: appId });
  const signingInput = `${header}.${payload}`;
  const key = await crypto.subtle.importKey(
    'pkcs8',
    parsePkcs8PrivateKey(privateKey),
    { hash: 'SHA-256', name: 'RSASSA-PKCS1-v1_5' },
    false,
    ['sign'],
  );
  const signature = await crypto.subtle.sign(
    { name: 'RSASSA-PKCS1-v1_5' },
    key,
    new TextEncoder().encode(signingInput),
  );

  return `${signingInput}.${base64UrlEncode(new Uint8Array(signature))}`;
}

function schedulerConfiguration(environment: SchedulerEnvironment): SchedulerConfiguration {
  const appId = environment.GITHUB_APP_ID?.trim();
  const installationId = environment.GITHUB_APP_INSTALLATION_ID?.trim();
  const privateKey = environment.GITHUB_APP_PRIVATE_KEY;
  if (!/^[1-9][0-9]*$/.test(appId ?? '') ||
    !/^[1-9][0-9]*$/.test(installationId ?? '') ||
    privateKey === undefined ||
    privateKey.trim().length === 0) {
    throw new SchedulerFailure('configuration');
  }

  return { appId: appId!, installationId: installationId!, privateKey };
}

function githubHeaders(authorization: string): HeadersInit {
  return {
    accept: 'application/vnd.github+json',
    authorization: `Bearer ${authorization}`,
    'x-github-api-version': GITHUB_API_VERSION,
  };
}

async function installationAccessToken(
  configuration: SchedulerConfiguration,
  dependencies: SchedulerDependencies,
): Promise<string> {
  const appJwt = await dependencies.createAppJwt(configuration.appId, configuration.privateKey, dependencies.now());
  let response: Response;
  try {
    response = await dependencies.fetch(
      `${GITHUB_API_URL}/app/installations/${configuration.installationId}/access_tokens`,
      { headers: githubHeaders(appJwt), method: 'POST' },
    );
  } catch {
    throw new SchedulerFailure('token-request');
  }

  if (!response.ok) throw new SchedulerFailure('token-request', response.status);

  try {
    const payload: unknown = await response.json();
    if (typeof payload !== 'object' || payload === null ||
      !('token' in payload) || typeof payload.token !== 'string' || payload.token.length === 0) {
      throw new SchedulerFailure('token-response');
    }
    return payload.token;
  } catch (error) {
    if (error instanceof SchedulerFailure) throw error;
    throw new SchedulerFailure('token-response');
  }
}

export async function dispatchPagesWorkflow(
  environment: SchedulerEnvironment,
  dependencies: SchedulerDependencies = {
    createAppJwt: createGitHubAppJwt,
    fetch,
    now: Date.now,
  },
): Promise<number> {
  const configuration = schedulerConfiguration(environment);
  const accessToken = await installationAccessToken(configuration, dependencies);
  let response: Response;
  try {
    response = await dependencies.fetch(
      `${GITHUB_API_URL}/repos/${REPOSITORY}/actions/workflows/${WORKFLOW}/dispatches`,
      {
        body: JSON.stringify({ ref: BRANCH }),
        headers: {
          ...githubHeaders(accessToken),
          'content-type': 'application/json',
        },
        method: 'POST',
      },
    );
  } catch {
    throw new SchedulerFailure('dispatch-request');
  }

  if (!response.ok) throw new SchedulerFailure('dispatch-request', response.status);
  return response.status;
}

function asSafeFailure(error: unknown): SchedulerFailure {
  if (error instanceof SchedulerFailure) return error;
  return new SchedulerFailure('unexpected');
}

export async function runScheduledDispatch(
  environment: SchedulerEnvironment,
  dependencies: SchedulerDependencies,
  logger: SafeLogger,
): Promise<'disabled' | 'dispatched'> {
  if (environment.SCHEDULER_ENABLED !== 'true') {
    logger.info('Pages scheduler is disabled.');
    return 'disabled';
  }

  try {
    const status = await dispatchPagesWorkflow(environment, dependencies);
    logger.info('Pages scheduler dispatch accepted.', { status });
    return 'dispatched';
  } catch (error) {
    const failure = asSafeFailure(error);
    logger.error('Pages scheduler dispatch failed.', {
      operation: failure.code,
      ...(failure.status === undefined ? {} : { status: failure.status }),
    });
    throw failure;
  }
}

const worker: ExportedHandler<SchedulerEnvironment> = {
  fetch() {
    return new Response('Not found.', { status: 404 });
  },
  scheduled(_controller, environment, context) {
    context.waitUntil(runScheduledDispatch(environment, {
      createAppJwt: createGitHubAppJwt,
      fetch,
      now: Date.now,
    }, console));
  },
};

export default worker;
