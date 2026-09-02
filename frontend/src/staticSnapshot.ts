import {
  MarketSnapshotParseError,
  parseMarketSnapshot,
  type MarketSnapshot,
} from './marketSnapshot';

export const MAXIMUM_SNAPSHOT_AGE_MS = 30 * 60 * 1_000;

export type SnapshotFreshness = 'fresh' | 'delayed' | 'future';

export type StaticSnapshotLoadState =
  | { kind: 'loading' }
  | { kind: 'ready'; snapshot: MarketSnapshot }
  | { kind: 'unavailable'; message: string }
  | { kind: 'incompatible'; message: string }
  | { kind: 'malformed'; message: string };

export function resolveMarketSnapshotUrl(
  baseUrl: string,
  configuredPath: string | undefined,
  currentOrigin: string,
): string {
  const deploymentBase = new URL(baseUrl, currentOrigin);
  const path = configuredPath?.trim() || 'market-snapshot.json';
  const resolved = new URL(path, deploymentBase);
  const deploymentBasePath = deploymentBase.pathname.endsWith('/')
    ? deploymentBase.pathname
    : `${deploymentBase.pathname}/`;
  if (resolved.origin !== deploymentBase.origin || !resolved.pathname.startsWith(deploymentBasePath)) {
    throw new Error('The configured market snapshot path must stay within this static site deployment base.');
  }

  return resolved.toString();
}

export async function loadStaticMarketSnapshot(
  url: string,
  request: typeof fetch = fetch,
): Promise<StaticSnapshotLoadState> {
  let response: Response;
  try {
    response = await request(url, { cache: 'no-store' });
  } catch {
    return { kind: 'unavailable', message: 'The published market snapshot could not be reached. Recommendations are unavailable.' };
  }

  if (!response.ok) {
    return { kind: 'unavailable', message: 'The published market snapshot is unavailable. Recommendations are unavailable.' };
  }

  let payload: unknown;
  try {
    payload = await response.json();
  } catch {
    return { kind: 'malformed', message: 'The published market snapshot is not valid JSON. Recommendations are unavailable.' };
  }

  try {
    return { kind: 'ready', snapshot: parseMarketSnapshot(payload) };
  } catch (error) {
    if (error instanceof MarketSnapshotParseError && error.kind === 'incompatible-snapshot') {
      return { kind: 'incompatible', message: 'The published market snapshot is not compatible with this version of Tyrian Ledger. Recommendations are unavailable.' };
    }

    return { kind: 'malformed', message: 'The published market snapshot is incomplete or malformed. Recommendations are unavailable.' };
  }
}

export function classifySnapshotFreshness(generatedAtUtc: string, nowMs: number): SnapshotFreshness {
  const generatedAtMs = Date.parse(generatedAtUtc);
  if (generatedAtMs > nowMs) return 'future';
  return nowMs - generatedAtMs > MAXIMUM_SNAPSHOT_AGE_MS ? 'delayed' : 'fresh';
}

export function formatSnapshotAge(generatedAtUtc: string, nowMs: number): string {
  const generatedAtMs = Date.parse(generatedAtUtc);
  if (generatedAtMs > nowMs) return 'The snapshot timestamp is ahead of this browser clock.';

  const wholeMinutes = Math.floor((nowMs - generatedAtMs) / 60_000);
  if (wholeMinutes === 0) return 'less than one minute';
  return `${wholeMinutes} minute${wholeMinutes === 1 ? '' : 's'}`;
}

export function millisecondsUntilSnapshotExpiry(generatedAtUtc: string, nowMs: number): number {
  return Math.max(0, Date.parse(generatedAtUtc) + MAXIMUM_SNAPSHOT_AGE_MS - nowMs + 1);
}
