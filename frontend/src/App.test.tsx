import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import {
  formatCopper,
  formatModeledRoi,
  M9_SETTINGS_STORAGE_KEY,
  SCAN_STATUS_POLL_INTERVAL_MS,
  type CompletedScanResult,
  type Recommendation,
  type ScanSnapshot,
  validateSettings,
} from './m9';

const runningSnapshot: ScanSnapshot = {
  state: 'running',
  progress: { stage: 'reading-finalist-listings', finalistCount: 2 },
  isRetryable: false,
  result: null,
};

const cancelledSnapshot: ScanSnapshot = {
  state: 'cancelled',
  progress: null,
  isRetryable: true,
  result: null,
};

const rateLimitedSnapshot: ScanSnapshot = {
  state: 'rate-limited',
  progress: null,
  isRetryable: true,
  result: null,
};

const incompleteSnapshot: ScanSnapshot = {
  state: 'complete',
  progress: null,
  isRetryable: false,
  result: null,
};

function recommendation(rank: number, route: Recommendation['route']): Recommendation {
  return {
    rank,
    itemId: rank,
    itemName: `Item ${rank}`,
    route,
    quantity: 2,
    buyUnitPriceCopper: 1_000,
    saleUnitPriceCopper: 2_000,
    buyOrderReserveCopper: 2_000,
    grossSaleCopper: 4_000,
    listingFeeCopper: 200,
    exchangeFeeCopper: 400,
    netSaleProceedsCopper: 3_400,
    totalCostCopper: 2_200,
    modeledProfitCopper: 1_400,
    modeledRoi: { profitCopper: 1_400, totalCostCopper: 2_200 },
    scanCompletedAtUtc: '2026-08-31T16:00:00Z',
    routeEvidence: {
      sellerQuantityAtOrBelowBuyPrice: route === 'can-act-now' ? 2 : 1,
      coversSelectedQuantity: route === 'can-act-now',
    },
    assumptions: [
      'current-order-book-snapshot-only',
      'manual-in-game-orders-required',
      'no-execution-sale-or-profit-guarantee',
    ],
  };
}

function completeSnapshot(recommendations: Recommendation[] = [recommendation(1, 'place-order-and-wait')]): ScanSnapshot {
  const result: CompletedScanResult = {
    capitalCopper: 123_456,
    riskProfile: 'balanced',
    spendCapCopper: 30_864,
    scanCompletedAtUtc: '2026-08-31T16:00:00Z',
    canActNow: recommendations.filter((item) => item.route === 'can-act-now'),
    placeOrderAndWait: recommendations.filter((item) => item.route === 'place-order-and-wait'),
  };
  return { state: 'complete', progress: null, isRetryable: false, result };
}

function response(snapshot: ScanSnapshot, status = 200): Response {
  return { ok: status >= 200 && status < 300, status, json: async () => snapshot } as unknown as Response;
}

function storeSettings() {
  window.localStorage.setItem(M9_SETTINGS_STORAGE_KEY, JSON.stringify({
    capital: { gold: '12', silver: '34', copper: '56' },
    riskProfile: 'balanced',
  }));
}

beforeEach(() => {
  window.localStorage.clear();
  window.history.replaceState({}, '', '/');
  vi.stubGlobal('fetch', vi.fn());
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('M9 beginner experience', () => {
  it('shows guided setup without initiating browser API traffic on a first visit', () => {
    const fetchMock = vi.mocked(fetch);
    render(<App />);

    expect(screen.getByText('A short guided setup')).toBeVisible();
    expect(screen.getByText(/You will always create every buy order and sell listing yourself/i)).toBeVisible();
    expect(screen.getByRole('button', { name: 'Set up my capital and risk' })).toBeVisible();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('validates denomination values and risk selection without floating-point conversion', () => {
    const invalid = validateSettings({ gold: '-1', silver: '100', copper: '2.5' }, null);
    expect(invalid.settings).toBeUndefined();
    expect(invalid.errors).toMatchObject({
      gold: 'Gold must be a non-negative whole number.',
      silver: 'Silver must be between 0 and 99.',
      copper: 'Copper must be a non-negative whole number.',
      riskProfile: 'Choose the risk level that feels right for you.',
    });

    const valid = validateSettings({ gold: '00012', silver: '034', copper: '056' }, 'balanced');
    expect(valid.settings).toMatchObject({
      capital: { gold: '12', silver: '34', copper: '56' },
      capitalCopper: 123_456,
      riskProfile: 'balanced',
    });
    expect(validateSettings({ gold: '900719925475', silver: '0', copper: '0' }, 'cautious').errors.gold).toContain('too large');
    expect(formatCopper(123_456)).toBe('12g 34s 56c');
    expect(formatModeledRoi({ profitCopper: 1_400, totalCostCopper: 2_000 })).toBe('70.0%');
  });

  it('saves only canonical settings and makes the saved choice ready for a scan', () => {
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: 'Set up my capital and risk' }));

    fireEvent.change(screen.getByLabelText('Gold'), { target: { value: '00012' } });
    fireEvent.change(screen.getByLabelText('Silver'), { target: { value: '034' } });
    fireEvent.change(screen.getByLabelText('Copper'), { target: { value: '056' } });
    fireEvent.click(screen.getByRole('radio', { name: /Balanced/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));

    expect(screen.getByRole('heading', { name: 'Recommendations' })).toBeVisible();
    expect(screen.getByText('12g 34s 56c')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Scan the market' })).toBeEnabled();
    expect(JSON.parse(window.localStorage.getItem(M9_SETTINGS_STORAGE_KEY) ?? '{}')).toEqual({
      capital: { gold: '12', silver: '34', copper: '56' },
      riskProfile: 'balanced',
    });
    expect(window.localStorage.length).toBe(1);
  });

  it('polls only an active player-started scan, groups complete results, and caps cards at five', async () => {
    vi.useFakeTimers();
    storeSettings();
    const recommendations = [
      recommendation(1, 'can-act-now'), recommendation(2, 'place-order-and-wait'),
      recommendation(3, 'can-act-now'), recommendation(4, 'place-order-and-wait'),
      recommendation(5, 'can-act-now'), recommendation(6, 'place-order-and-wait'),
    ];
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(response(runningSnapshot));
    fetchMock.mockResolvedValueOnce(response(completeSnapshot(recommendations)));

    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: 'Scan the market' }));
    await act(async () => {});

    expect(screen.getByRole('status')).toHaveTextContent('2 finalists need detailed checks');
    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/recommendations/scan', expect.objectContaining({
      method: 'POST', body: JSON.stringify({ capitalCopper: 123_456, riskProfile: 'balanced' }),
    }));

    await act(async () => { await vi.advanceTimersByTimeAsync(SCAN_STATUS_POLL_INTERVAL_MS); });

    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/recommendations/scan');
    expect(screen.getByRole('heading', { name: 'Can act now' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Place an order and wait' })).toBeVisible();
    expect(screen.getAllByRole('article')).toHaveLength(5);
    expect(screen.getByRole('heading', { name: 'Item 1' })).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Item 6' })).not.toBeInTheDocument();
    expect(screen.getAllByText('Listing fee')).toHaveLength(5);
    expect(screen.getAllByText('Total cost (buy order + listing fee)')).toHaveLength(5);
    expect(screen.getAllByText('63.6%')).toHaveLength(5);
    expect(screen.getAllByRole('heading', { name: 'Manual in-game steps' })).toHaveLength(5);
    expect(screen.queryByRole('button', { name: /copy/i })).not.toBeInTheDocument();
  });

  it('removes stale results immediately for cancellation and reports no recommendations', async () => {
    storeSettings();
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(response(completeSnapshot()));
    fetchMock.mockResolvedValueOnce(response(runningSnapshot));
    fetchMock.mockResolvedValueOnce(response(cancelledSnapshot));
    render(<App />);

    fireEvent.click(screen.getByRole('button', { name: 'Scan the market' }));
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Item 1' })).toBeVisible());

    fireEvent.click(screen.getByRole('button', { name: 'Scan the market' }));
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Item 1' })).not.toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Cancel scan' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Scan cancelled. No recommendations were kept.'));
    expect(screen.queryByRole('heading', { name: 'Item 1' })).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenLastCalledWith('/api/recommendations/scan', { method: 'DELETE' });
  });

  it('shows a rate-limit outcome without cards and retries only when the player asks', async () => {
    storeSettings();
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(response(rateLimitedSnapshot));
    fetchMock.mockResolvedValueOnce(response(completeSnapshot()));
    render(<App />);

    fireEvent.click(screen.getByRole('button', { name: 'Scan the market' }));
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('asked us to slow down'));
    expect(screen.queryByRole('article')).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: 'Retry scan' }));
    await waitFor(() => expect(screen.getByRole('heading', { name: 'Item 1' })).toBeVisible());
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('treats an incomplete result as a failed scan and renders a safe empty completion', async () => {
    storeSettings();
    const fetchMock = vi.mocked(fetch);
    fetchMock.mockResolvedValueOnce(response(incompleteSnapshot));
    fetchMock.mockResolvedValueOnce(response(completeSnapshot([])));
    render(<App />);

    fireEvent.click(screen.getByRole('button', { name: 'Scan the market' }));
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('The scan could not be started.'));
    expect(screen.queryByRole('article')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Retry scan' }));
    await waitFor(() => expect(screen.getByRole('heading', { name: 'No suggestions right now' })).toBeVisible());
    expect(screen.queryByRole('article')).not.toBeInTheDocument();
  });

  it('keeps retired browser paths unavailable', () => {
    window.history.replaceState({}, '', '/history');
    render(<App />);

    expect(screen.getByTestId('unavailable-route')).toHaveTextContent('Route unavailable');
    expect(screen.getByRole('link', { name: 'Go to Recommendations' })).toHaveAttribute('href', '/recommendations');
  });
});
