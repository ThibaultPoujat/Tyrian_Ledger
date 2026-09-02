import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import {
  formatCopper,
  formatModeledRoi,
  M9_SETTINGS_STORAGE_KEY,
  validateSettings,
} from './m9';

const generatedAtUtc = '2026-09-01T12:00:00.0000000Z';
let nowMs = Date.parse('2026-09-01T12:30:00.000Z');

function snapshot(overrides: Record<string, unknown> = {}): unknown {
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
    ...overrides,
  };
}

function response(payload: unknown, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => payload } as unknown as Response;
}

function storeSettings(capital = { gold: '12', silver: '0', copper: '0' }, riskProfile = 'balanced') {
  window.localStorage.setItem(M9_SETTINGS_STORAGE_KEY, JSON.stringify({ capital, riskProfile }));
}

beforeEach(() => {
  nowMs = Date.parse('2026-09-01T12:30:00.000Z');
  window.localStorage.clear();
  window.history.replaceState({}, '', '/');
  vi.spyOn(Date, 'now').mockImplementation(() => nowMs);
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(snapshot())));
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('static snapshot experience', () => {
  it('shows an accessible loading state before the browser receives the published snapshot', async () => {
    let resolveRequest: ((value: Response) => void) | undefined;
    vi.mocked(fetch).mockImplementationOnce(() => new Promise<Response>((resolve) => { resolveRequest = resolve; }));

    render(<App />);

    expect(screen.getByRole('status')).toHaveTextContent('Loading the published market snapshot');
    expect(screen.getByText('Start here')).toBeVisible();
    resolveRequest?.(response(snapshot()));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Compatible snapshot loaded.'));
  });

  it('uses browser-local BigInt preferences to render fresh compatible snapshot recommendations', async () => {
    storeSettings();
    render(<App />);

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Synthetic public item' })).toBeVisible());
    expect(screen.getByRole('status')).toHaveTextContent('Generated:');
    expect(screen.getByRole('status')).toHaveTextContent('Data age: 30 minutes.');
    expect(screen.getByText('12g 0s 0c')).toBeVisible();
    expect(screen.queryAllByRole('button', { name: /scan/i })).toHaveLength(0);
    expect(vi.mocked(fetch)).toHaveBeenCalledTimes(1);
    expect(String(vi.mocked(fetch).mock.calls[0][0])).toContain('/market-snapshot.json');
  });

  it('treats a snapshot older than 30 minutes as delayed and suppresses every recommendation', async () => {
    nowMs = Date.parse('2026-09-01T12:30:00.001Z');
    storeSettings();
    render(<App />);

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Snapshot refresh is delayed.'));
    expect(screen.getByRole('alert')).toHaveTextContent('30 minutes old');
    expect(screen.queryByRole('heading', { name: 'Synthetic public item' })).not.toBeInTheDocument();
  });

  it('treats future timestamps as non-actionable and suppresses every recommendation', async () => {
    nowMs = Date.parse('2026-09-01T11:59:59.999Z');
    storeSettings();
    render(<App />);

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Snapshot timestamp cannot be trusted.'));
    expect(screen.queryByRole('heading', { name: 'Synthetic public item' })).not.toBeInTheDocument();
  });

  it.each([
    ['incompatible', snapshot({ contractVersion: 2 }), 'not compatible'],
    ['malformed', snapshot({ candidates: [{ itemId: 1 }] }), 'incomplete or malformed'],
    ['unavailable', {}, 'snapshot is unavailable'],
  ])('shows an actionable $0 snapshot message without cards', async (_state, payload, expectedMessage) => {
    storeSettings();
    vi.mocked(fetch).mockResolvedValueOnce(response(payload, expectedMessage === 'snapshot is unavailable' ? 404 : 200));
    render(<App />);

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(expectedMessage));
    expect(screen.queryByRole('heading', { name: 'Synthetic public item' })).not.toBeInTheDocument();
  });

  it('recalculates locally after a preference change without requesting another snapshot', async () => {
    storeSettings();
    render(<App />);

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Synthetic public item' })).toBeVisible());
    fireEvent.click(screen.getByRole('link', { name: 'Settings' }));
    fireEvent.change(screen.getByLabelText('Gold'), { target: { value: '4' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save preferences' }));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'No suggestions right now' })).toBeVisible());
    expect(screen.getByText('4g 0s 0c')).toBeVisible();
    expect(vi.mocked(fetch)).toHaveBeenCalledTimes(1);
  });

  it('keeps accessible settings validation and exact integer-copper formatting', () => {
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: 'Set up capital and risk' }));
    fireEvent.change(screen.getByLabelText('Gold'), { target: { value: '-1' } });
    fireEvent.change(screen.getByLabelText('Silver'), { target: { value: '100' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save preferences' }));

    expect(screen.getByText('Gold must be a non-negative whole number.')).toBeVisible();
    expect(validateSettings({ gold: '00012', silver: '034', copper: '056' }, 'balanced').settings).toMatchObject({
      capital: { gold: '12', silver: '34', copper: '56' }, capitalCopper: 123_456n, riskProfile: 'balanced',
    });
    expect(formatCopper(123_456n)).toBe('12g 34s 56c');
    expect(formatModeledRoi({ profitCopper: 1_400n, totalCostCopper: 2_000n })).toBe('70.0%');
  });

  it('resets only local preferences and keeps unavailable routes unavailable', async () => {
    storeSettings();
    window.localStorage.setItem('unrelated-setting', 'keep');
    render(<App />);

    fireEvent.click(screen.getByRole('link', { name: 'Settings' }));
    fireEvent.click(screen.getByRole('button', { name: 'Clear local preferences' }));
    expect(screen.getByText('Start here')).toBeVisible();
    expect(window.localStorage.getItem(M9_SETTINGS_STORAGE_KEY)).toBeNull();
    expect(window.localStorage.getItem('unrelated-setting')).toBe('keep');

    cleanup();
    window.history.replaceState({}, '', '/#/history');
    render(<App />);
    expect(screen.getByTestId('unavailable-route')).toHaveTextContent('Route unavailable');
  });

  it('opens a transparent manual trade plan and restores keyboard focus when it closes', async () => {
    storeSettings();
    render(<App />);

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Synthetic public item' })).toBeVisible());
    const planButton = screen.getByRole('button', { name: 'View manual trade plan' });
    planButton.focus();
    fireEvent.click(planButton);

    const dialog = screen.getByRole('dialog', { name: 'Synthetic public item' });
    expect(dialog).toHaveTextContent('Pricing and fees');
    expect(dialog).toHaveTextContent('Liquidity guard');
    expect(dialog).toHaveTextContent('Follow this checklist manually');
    expect(screen.getByRole('button', { name: 'Close manual trade plan' })).toHaveFocus();

    fireEvent.keyDown(dialog, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(planButton).toHaveFocus();
  });

  it('closes an open trade plan as soon as the published snapshot expires', async () => {
    const nativeSetTimeout = window.setTimeout;
    let expireSnapshot: (() => void) | undefined;
    nowMs = Date.parse(generatedAtUtc) + 30 * 60_000 - 1;
    vi.spyOn(window, 'setTimeout').mockImplementation(((handler: TimerHandler, timeout?: number) => {
      if (timeout === 2 && typeof handler === 'function') {
        expireSnapshot = () => handler();
        return 0 as unknown as number;
      }
      return nativeSetTimeout(handler, timeout);
    }) as typeof window.setTimeout);
    storeSettings();
    render(<App />);

    await waitFor(() => expect(screen.getByRole('button', { name: 'View manual trade plan' })).toBeVisible());
    fireEvent.click(screen.getByRole('button', { name: 'View manual trade plan' }));
    expect(screen.getByRole('dialog', { name: 'Synthetic public item' })).toBeVisible();
    nowMs += 2;
    act(() => expireSnapshot?.());

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByRole('alert')).toHaveTextContent('Snapshot refresh is delayed.');
  });

  it('closes an open trade plan when hash history moves to Settings', async () => {
    storeSettings();
    render(<App />);

    await waitFor(() => expect(screen.getByRole('button', { name: 'View manual trade plan' })).toBeVisible());
    fireEvent.click(screen.getByRole('button', { name: 'View manual trade plan' }));
    expect(screen.getByRole('dialog', { name: 'Synthetic public item' })).toBeVisible();
    window.history.pushState({}, '', '#/settings');
    window.dispatchEvent(new Event('hashchange'));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByRole('heading', { name: 'Set your trading guardrails' })).toBeVisible();
  });
});
