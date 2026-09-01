import { describe, expect, it, vi } from 'vitest';
import {
  classifySnapshotFreshness,
  formatSnapshotAge,
  loadStaticMarketSnapshot,
  MAXIMUM_SNAPSHOT_AGE_MS,
  millisecondsUntilSnapshotExpiry,
  resolveMarketSnapshotUrl,
} from './staticSnapshot';

const generatedAtUtc = '2026-09-01T12:00:00.0000000Z';

function snapshot(): unknown {
  return {
    contractVersion: 1,
    generatedAtUtc,
    compatibility: { moneyUnit: 'copper', recommendationPolicyVersion: 'm9-v1', normalStackLimit: 250 },
    capturePolicy: { requestsPerSecond: 2, maxConcurrentRequests: 2, burstBudget: 20 },
    candidates: [{
      itemId: 900001,
      itemName: 'Synthetic public item',
      buys: [{ listingCount: 3, quantity: 100, unitPriceInCopper: 1000 }],
      sells: [{ listingCount: 3, quantity: 100, unitPriceInCopper: 1500 }],
    }],
  };
}

function jsonResponse(payload: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => payload } as unknown as Response;
}

describe('static market snapshot loading', () => {
  it('resolves the configured path below the static deployment base', () => {
    expect(resolveMarketSnapshotUrl('/Tyrian_Ledger/', undefined, 'https://example.github.io')).toBe(
      'https://example.github.io/Tyrian_Ledger/market-snapshot.json',
    );
    expect(resolveMarketSnapshotUrl('/Tyrian_Ledger/', 'assets/current.json', 'https://example.github.io')).toBe(
      'https://example.github.io/Tyrian_Ledger/assets/current.json',
    );
    expect(() => resolveMarketSnapshotUrl('/Tyrian_Ledger/', '../other-repo/market-snapshot.json', 'https://example.github.io')).toThrow(
      'must stay within this static site deployment base',
    );
    expect(() => resolveMarketSnapshotUrl('/', 'https://api.guildwars2.com/v2/items', 'https://example.github.io')).toThrow(
      'must stay within this static site deployment base',
    );
  });

  it('keeps a snapshot actionable through the exact 30-minute boundary', () => {
    const generatedAtMs = Date.parse(generatedAtUtc);
    expect(classifySnapshotFreshness(generatedAtUtc, generatedAtMs + MAXIMUM_SNAPSHOT_AGE_MS)).toBe('fresh');
    expect(classifySnapshotFreshness(generatedAtUtc, generatedAtMs + MAXIMUM_SNAPSHOT_AGE_MS + 1)).toBe('delayed');
    expect(millisecondsUntilSnapshotExpiry(generatedAtUtc, generatedAtMs + MAXIMUM_SNAPSHOT_AGE_MS)).toBe(1);
    expect(formatSnapshotAge(generatedAtUtc, generatedAtMs + MAXIMUM_SNAPSHOT_AGE_MS)).toBe('30 minutes');
  });

  it('marks future-dated snapshots non-actionable instead of treating them as fresh', () => {
    const generatedAtMs = Date.parse(generatedAtUtc);
    expect(classifySnapshotFreshness(generatedAtUtc, generatedAtMs - 1)).toBe('future');
    expect(formatSnapshotAge(generatedAtUtc, generatedAtMs - 1)).toContain('ahead of this browser clock');
  });

  it('loads valid compatible snapshot data through the supplied request function', async () => {
    const request = vi.fn().mockResolvedValue(jsonResponse(snapshot()));
    await expect(loadStaticMarketSnapshot('https://example.github.io/market-snapshot.json', request)).resolves.toMatchObject({
      kind: 'ready',
      snapshot: { generatedAtUtc, candidates: [{ itemName: 'Synthetic public item' }] },
    });
    expect(request).toHaveBeenCalledWith('https://example.github.io/market-snapshot.json');
  });

  it('distinguishes unavailable, incompatible, malformed, and invalid JSON snapshot states', async () => {
    await expect(loadStaticMarketSnapshot('/market-snapshot.json', vi.fn().mockResolvedValue(jsonResponse({}, 404)))).resolves.toMatchObject({ kind: 'unavailable' });
    await expect(loadStaticMarketSnapshot('/market-snapshot.json', vi.fn().mockResolvedValue(jsonResponse({ ...snapshot() as object, contractVersion: 2 })))).resolves.toMatchObject({ kind: 'incompatible' });
    await expect(loadStaticMarketSnapshot('/market-snapshot.json', vi.fn().mockResolvedValue(jsonResponse({ ...snapshot() as object, candidates: [{ itemId: 1 }] })))).resolves.toMatchObject({ kind: 'malformed' });
    await expect(loadStaticMarketSnapshot('/market-snapshot.json', vi.fn().mockResolvedValue({ ok: true, json: async () => { throw new Error('invalid JSON'); } } as unknown as Response))).resolves.toMatchObject({ kind: 'malformed' });
  });
});
