import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import type {
  AccountAccessStatus,
  DashboardOpportunitiesResponse,
  DashboardOpportunityDetail,
  MarketResearchWatchlist,
  OperationHistoryStatistics,
  UserSessionPreferences,
} from './dashboardApi';

const opportunityDetail: DashboardOpportunityDetail = {
  requestedQuantity: 5,
  analyzedAtUtc: '2026-08-27T12:00:00Z',
  acquisition: {
    requestedQuantity: 5,
    filledQuantity: 5,
    isFullyFilled: true,
    totalValueCopper: 800,
    priceImpactCopper: 300,
  },
  exit: {
    requestedQuantity: 5,
    filledQuantity: 5,
    isFullyFilled: true,
    totalValueCopper: 1_500,
    priceImpactCopper: 0,
  },
  fees: {
    listingBasisPoints: 0,
    listingRounding: 'down',
    listingFeeCopper: 0,
    exchangeBasisPoints: 0,
    exchangeRounding: 'down',
    exchangeFeeCopper: 0,
  },
  financials: {
    acquisitionCostCopper: 800,
    grossSaleValueCopper: 1_500,
    netSaleProceedsCopper: 1_500,
    capitalRequiredCopper: 800,
    modeledNetProfitCopper: 700,
    returnOnInvestmentBasisPoints: 8_750,
  },
  liquidity: {
    acquisitionFilledQuantity: 5,
    liquidationFilledQuantity: 5,
    isFullyAcquirable: true,
    isFullyLiquidatable: true,
    acquisitionPriceImpactCopper: 300,
    liquidationPriceImpactCopper: 0,
    totalPriceImpactCopper: 300,
  },
  freshness: 'current',
  capturedAtUtc: '2026-08-27T11:59:00Z',
  expiresAtUtc: '2026-08-27T12:04:00Z',
  confidence: 'normal',
};

const dashboardResponse: DashboardOpportunitiesResponse = {
  isSampleData: true,
  sourceDescription: 'Deterministic local sample data. No live market scan was performed.',
  generatedAtUtc: '2026-08-27T12:00:00Z',
  opportunities: [
    {
      itemId: 900_004,
      label: 'Sample market flip #900004',
      strategy: 'market-flip',
      effortCategory: 'medium',
      rank: 1,
      scoreBasisPoints: 9_000,
      capitalRequiredCopper: 800,
      modeledNetProfitCopper: 700,
      returnOnInvestmentBasisPoints: 8_750,
      liquidityPriceImpactCopper: 300,
      confidence: 'normal',
      freshness: 'current',
      capturedAtUtc: '2026-08-27T11:59:00Z',
      detail: opportunityDetail,
    },
    {
      itemId: 900_003,
      label: 'Sample market flip #900003',
      strategy: 'market-flip',
      effortCategory: 'ongoing-patient',
      rank: 2,
      scoreBasisPoints: 7_000,
      capitalRequiredCopper: 800,
      modeledNetProfitCopper: 600,
      returnOnInvestmentBasisPoints: 7_500,
      liquidityPriceImpactCopper: 0,
      confidence: 'reduced',
      freshness: 'stale',
      capturedAtUtc: '2026-08-27T11:30:00Z',
      detail: {
        ...opportunityDetail,
        freshness: 'stale',
        capturedAtUtc: '2026-08-27T11:30:00Z',
        confidence: 'reduced',
      },
    },
    {
      itemId: 900_001,
      label: 'Sample market flip #900001',
      strategy: 'market-flip',
      effortCategory: 'very-low',
      rank: 3,
      scoreBasisPoints: 6_000,
      capitalRequiredCopper: 500,
      modeledNetProfitCopper: 300,
      returnOnInvestmentBasisPoints: 6_000,
      liquidityPriceImpactCopper: 0,
      confidence: 'normal',
      freshness: 'current',
      capturedAtUtc: '2026-08-27T11:59:00Z',
      detail: opportunityDetail,
    },
    {
      itemId: 900_002,
      label: 'Sample market flip #900002',
      strategy: 'market-flip',
      effortCategory: 'low',
      rank: 4,
      scoreBasisPoints: 5_000,
      capitalRequiredCopper: 1_000,
      modeledNetProfitCopper: 400,
      returnOnInvestmentBasisPoints: 4_000,
      liquidityPriceImpactCopper: 0,
      confidence: 'normal',
      freshness: 'current',
      capturedAtUtc: '2026-08-27T11:59:00Z',
      detail: opportunityDetail,
    },
    {
      itemId: 900_005,
      label: 'Sample craft #900005',
      strategy: 'crafting',
      effortCategory: 'high',
      rank: 5,
      scoreBasisPoints: 4_000,
      capitalRequiredCopper: 1_500,
      modeledNetProfitCopper: 250,
      returnOnInvestmentBasisPoints: 1_666,
      liquidityPriceImpactCopper: 0,
      confidence: 'normal',
      freshness: 'current',
      capturedAtUtc: '2026-08-27T11:59:00Z',
      detail: opportunityDetail,
    },
  ],
};

const defaultPreferences: UserSessionPreferences = {
  capitalLimitCopper: null,
  minimumProfitCopper: null,
  riskPreference: 'all',
  strategyPreference: 'all',
  allocationPercent: 100,
};

const marketResearchWatchlist: MarketResearchWatchlist = {
  maximumWatchlistItemCount: 25,
  trackedItemCount: 2,
  items: [
    {
      itemId: 19721,
      coverage: {
        observationCount: 30,
        firstCapturedAtUtc: '2026-08-01T00:00:00Z',
        lastCapturedAtUtc: '2026-08-02T05:00:00Z',
      },
      buyPrices: {
        observationCount: 30,
        tenthPercentileCopper: 100,
        medianCopper: 140,
        ninetiethPercentileCopper: 180,
      },
      sellPrices: {
        observationCount: 30,
        tenthPercentileCopper: 120,
        medianCopper: 160,
        ninetiethPercentileCopper: 200,
      },
      buyLiquidity: { observationCount: 30, coefficientOfVariationPercent: 25 },
      sellLiquidity: { observationCount: 30, coefficientOfVariationPercent: 10 },
    },
    {
      itemId: 46731,
      coverage: {
        observationCount: 4,
        firstCapturedAtUtc: '2026-08-01T00:00:00Z',
        lastCapturedAtUtc: '2026-08-01T03:00:00Z',
      },
      buyPrices: {
        observationCount: 4,
        tenthPercentileCopper: null,
        medianCopper: null,
        ninetiethPercentileCopper: null,
      },
      sellPrices: {
        observationCount: 4,
        tenthPercentileCopper: null,
        medianCopper: null,
        ninetiethPercentileCopper: null,
      },
      buyLiquidity: { observationCount: 4, coefficientOfVariationPercent: null },
      sellLiquidity: { observationCount: 4, coefficientOfVariationPercent: null },
    },
  ],
};

const populatedHistoryStatistics: OperationHistoryStatistics = {
  operationCount: 4,
  firstRecordedAtUtc: '2026-08-28T08:00:00Z',
  lastRecordedAtUtc: '2026-08-28T08:08:00Z',
  modeledNetProfit: {
    eligibleOperationCount: 2,
    totalCopper: 21,
  },
  realizedProfit: {
    eligibleOperationCount: 2,
    totalCopper: 40,
  },
  lifecycle: {
    completedOperationCount: 1,
    cancelledOperationCount: 1,
    terminalOperationCount: 2,
  },
};

const emptyHistoryStatistics: OperationHistoryStatistics = {
  operationCount: 0,
  firstRecordedAtUtc: null,
  lastRecordedAtUtc: null,
  modeledNetProfit: {
    eligibleOperationCount: 0,
    totalCopper: null,
  },
  realizedProfit: {
    eligibleOperationCount: 0,
    totalCopper: null,
  },
  lifecycle: {
    completedOperationCount: 0,
    cancelledOperationCount: 0,
    terminalOperationCount: 0,
  },
};

const missingCraftingPermissionAccess: AccountAccessStatus = {
  validationStatus: 'valid',
  keyId: 'synthetic-token-id-fragment',
  keyName: "<img src=x onerror=alert('synthetic')>",
  permissions: ['account', 'inventories'],
  features: [
    { feature: 'account-materials', isAvailable: true, missingPermissions: [] },
    { feature: 'account-crafting', isAvailable: false, missingPermissions: ['characters', 'unlocks'] },
  ],
};

const fetchMock = vi.fn();

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('market dashboard preferences and filters', () => {
  it('hydrates, saves, and applies a deterministic local preference profile', async () => {
    let currentPreferences = defaultPreferences;
    let dashboardRequestCount = 0;
    const filteredResponse: DashboardOpportunitiesResponse = {
      ...dashboardResponse,
      opportunities: [dashboardResponse.opportunities[0], dashboardResponse.opportunities[2]].map(
        (opportunity, index) => ({ ...opportunity, rank: index + 1 }),
      ),
    };
    fetchMock.mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = getFetchUrl(input);
      if (url === '/api/preferences/user-session' && init?.method === 'PUT') {
        currentPreferences = JSON.parse(init.body as string) as UserSessionPreferences;
        return Promise.resolve(successfulResponse(currentPreferences));
      }

      if (url === '/api/preferences/user-session') {
        return Promise.resolve(successfulResponse(currentPreferences));
      }

      dashboardRequestCount += 1;
      return Promise.resolve(successfulResponse(
        dashboardRequestCount === 1 ? dashboardResponse : filteredResponse,
      ));
    });

    render(<App />);
    await screen.findByRole('heading', { name: 'Ranked opportunities' });
    expect(screen.getByLabelText(/available capital/i)).toHaveValue(null);
    expect(screen.getByLabelText(/per-opportunity allocation/i)).toHaveValue(100);

    fireEvent.change(screen.getByLabelText(/available capital/i), { target: { value: '1600' } });
    fireEvent.change(screen.getByLabelText(/risk \/ confidence/i), { target: { value: 'normal' } });
    fireEvent.change(screen.getByLabelText(/per-opportunity allocation/i), { target: { value: '50' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save and apply preferences' }));

    await waitFor(() => expect(screen.getAllByTestId('opportunity-row')).toHaveLength(2));
    expectOpportunityRows(['Sample market flip #900004', 'Sample market flip #900001']);
    const saveCall = fetchMock.mock.calls.find(([, init]) => (init as RequestInit | undefined)?.method === 'PUT');
    expect(saveCall).toBeDefined();
    expect(JSON.parse((saveCall?.[1] as RequestInit).body as string)).toEqual({
      capitalLimitCopper: 1600,
      minimumProfitCopper: null,
      riskPreference: 'normal',
      strategyPreference: 'all',
      allocationPercent: 50,
    });
  });

  it('shows local validation feedback without sending invalid preferences', async () => {
    await renderReadyDashboard();

    fireEvent.change(screen.getByLabelText(/per-opportunity allocation/i), { target: { value: '0' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save and apply preferences' }));

    expect(screen.getByText('Per-opportunity allocation must be an integer from 1 through 100.')).toBeVisible();
    expect(fetchMock.mock.calls.some(([, init]) => (init as RequestInit | undefined)?.method === 'PUT')).toBe(false);
  });

  it('keeps freshness as a view-only filter after saved preferences are applied', async () => {
    await renderReadyDashboard();

    fireEvent.change(screen.getByLabelText(/^freshness$/i), { target: { value: 'stale' } });

    expectOpportunityRows(['Sample market flip #900003']);
  });

  it('requests a session-only effort shortlist and explains that categories are not time guarantees', async () => {
    const highEffortResponse: DashboardOpportunitiesResponse = {
      ...dashboardResponse,
      opportunities: [{ ...dashboardResponse.opportunities[4], rank: 1 }],
    };
    fetchMock.mockImplementation((input: RequestInfo | URL) => Promise.resolve(successfulResponse(
      getFetchUrl(input) === '/api/dashboard/opportunities?effortCategory=high'
        ? highEffortResponse
        : getFetchUrl(input) === '/api/dashboard/opportunities' ? dashboardResponse : defaultPreferences,
    )));

    render(<App />);
    await screen.findByRole('heading', { name: 'Ranked opportunities' });

    expect(screen.getByLabelText(/session effort/i)).toHaveValue('all');
    expect(screen.getByText(/rough planning labels, not time, execution, fill, or profit guarantees/i)).toBeVisible();
    fireEvent.change(screen.getByLabelText(/session effort/i), { target: { value: 'high' } });

    await waitFor(() => expectOpportunityRows(['Sample craft #900005']));
    expect(fetchMock.mock.calls.map(([input]) => getFetchUrl(input as RequestInfo | URL)))
      .toContain('/api/dashboard/opportunities?effortCategory=high');
    expect(fetchMock.mock.calls.some(([, init]) => (init as RequestInit | undefined)?.method === 'PUT')).toBe(false);
    expect(screen.getByText('High')).toBeVisible();
  });

  it('renders the selected opportunity detail as a modeled scenario', async () => {
    await renderReadyDashboard();

    fireEvent.click(screen.getByRole('button', {
      name: 'View details for Sample market flip #900004',
    }));

    const detail = screen.getByTestId('opportunity-detail');
    expect(detail).toHaveTextContent('Modeled scenario only.');
    expect(detail).toHaveTextContent('not an actual purchase, sale, fill, fee, or realized-profit outcome');
    expect(detail).toHaveTextContent('Take supplied sell levels: 5 of 5 items for 0g 8s 0c.');
    expect(detail).toHaveTextContent('Take supplied buy levels: 5 of 5 items for 0g 15s 0c gross.');
    expect(detail).toHaveTextContent('Capital required0g 8s 0c');
    expect(detail).toHaveTextContent('Modeled profit0g 7s 0c');
    expect(detail).toHaveTextContent('Modeled ROI87.50%');
    expect(detail).toHaveTextContent('Total price impact0g 3s 0c');
    expect(detail).toHaveTextContent('Human-readable calculation breakdown');
    expect(detail).toHaveTextContent('Data age');
  });
});

describe('historical market research', () => {
  it('discloses local coverage and withholds metrics when the observed sample is insufficient', async () => {
    fetchMock.mockImplementation((input: RequestInfo | URL) => Promise.resolve(successfulResponse(
      getFetchUrl(input) === '/api/dashboard/opportunities'
        ? dashboardResponse
        : getFetchUrl(input) === '/api/market-research/watchlist'
          ? marketResearchWatchlist
          : emptyHistoryStatistics,
    )));

    render(<App />);

    const research = await screen.findByTestId('market-research');
    expect(research).toHaveTextContent('Observed local prices and liquidity are descriptive evidence, not investment advice, a forecast, or a guarantee.');
    expect(research).toHaveTextContent('30 local samples: 2026-08-01T00:00:00.000Z through 2026-08-02T05:00:00.000Z.');
    expect(research).toHaveTextContent('P10 0g 1s 0c · median 0g 1s 40c · P90 0g 1s 80c');
    expect(research).toHaveTextContent('Insufficient local sample (4 observed).');
    expect(research).toHaveTextContent('missing values are not treated as zero');
  });

  it('adds, compares, and removes a small local research watchlist', async () => {
    let currentWatchlist: MarketResearchWatchlist = { ...marketResearchWatchlist, items: [marketResearchWatchlist.items[0]] };
    fetchMock.mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = getFetchUrl(input);
      if (url === '/api/market-research/watchlist' && init?.method === 'POST') {
        currentWatchlist = { ...marketResearchWatchlist };
        return Promise.resolve(noContentResponse());
      }

      if (url === '/api/market-research/watchlist/19721' && init?.method === 'DELETE') {
        currentWatchlist = { ...currentWatchlist, items: currentWatchlist.items.filter((item) => item.itemId !== 19721) };
        return Promise.resolve(noContentResponse());
      }

      return Promise.resolve(successfulResponse(
        url === '/api/dashboard/opportunities'
          ? dashboardResponse
          : url === '/api/market-research/watchlist'
            ? currentWatchlist
            : emptyHistoryStatistics,
      ));
    });

    render(<App />);
    await screen.findByTestId('market-research');
    expect(screen.getAllByTestId('research-row')).toHaveLength(1);

    fireEvent.change(screen.getByLabelText(/guild wars 2 item id/i), { target: { value: '46731' } });
    fireEvent.click(screen.getByRole('button', { name: 'Add to research watchlist' }));

    await waitFor(() => expect(screen.getAllByTestId('research-row')).toHaveLength(2));
    expect(screen.getByText('Item #19721')).toBeVisible();
    expect(screen.getByText('Item #46731')).toBeVisible();
    expect(screen.getByText('2 research items; 2 of 25 tracked')).toBeVisible();

    fireEvent.click(screen.getAllByRole('button', { name: 'Remove' })[0]);
    await waitFor(() => expect(screen.getAllByTestId('research-row')).toHaveLength(1));
    expect(screen.queryByText('Item #19721')).toBeNull();
  });
});

describe('market dashboard states', () => {
  it('shows an explicit loading state before the sample feed responds', () => {
    fetchMock.mockImplementation(() => new Promise(() => {}));

    render(<App />);

    expect(screen.getByRole('status')).toHaveTextContent('Loading local deterministic sample opportunities.');
  });

  it('shows an explicit error and retries the local sample feed', async () => {
    let firstDashboardRequest = true;
    fetchMock.mockImplementation((input: RequestInfo | URL) => {
      if (getFetchUrl(input) === '/api/dashboard/opportunities' && firstDashboardRequest) {
        firstDashboardRequest = false;
        return Promise.reject(new Error('Sample feed unavailable.'));
      }

      return Promise.resolve(successfulResponse(
        getFetchUrl(input) === '/api/dashboard/opportunities' ? dashboardResponse : defaultPreferences,
      ));
    });

    render(<App />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Dashboard data could not load');
    fireEvent.click(screen.getByRole('button', { name: 'Retry loading dashboard' }));

    expect(await screen.findByText('Sample market flip #900004')).toBeVisible();
  });
});

describe('account access status', () => {
  it('renders token metadata as text and identifies disabled features with missing permissions', async () => {
    fetchMock.mockImplementation((input: RequestInfo | URL) => Promise.resolve(successfulResponse(
      getFetchUrl(input) === '/api/dashboard/opportunities'
        ? dashboardResponse
        : getFetchUrl(input) === '/api/account/access'
          ? missingCraftingPermissionAccess
          : defaultPreferences,
    )));

    render(<App />);

    expect(await screen.findByText("<img src=x onerror=alert('synthetic')>")).toBeVisible();
    expect(screen.getByText(/granted permissions: account, inventories/i)).toBeVisible();
    expect(screen.getByText(/account materials enabled/i)).toBeVisible();
    expect(screen.getByText(/account crafting disabled — missing characters, unlocks/i)).toBeVisible();
    expect(document.querySelector('img')).toBeNull();
  });
});

describe('local account data control', () => {
  it('requires confirmation before clearing only account snapshots and explains retained local data', async () => {
    fetchMock.mockImplementation((input: RequestInfo | URL) => {
      const url = getFetchUrl(input);
      if (url === '/api/account/snapshots') {
        return Promise.resolve(noContentResponse());
      }

      return Promise.resolve(successfulResponse(
        url === '/api/dashboard/opportunities'
          ? dashboardResponse
          : url === '/api/history/statistics'
            ? emptyHistoryStatistics
            : defaultPreferences,
      ));
    });

    render(<App />);
    await screen.findByRole('heading', { name: 'Ranked opportunities' });

    expect(screen.getByText(/clearing them stays on this device and never uploads data/i)).toBeVisible();
    expect(screen.getByText(/saved operation history, preferences, public market cache, and your operating-system api key are not removed/i)).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Clear account snapshot data' }));

    expect(screen.getByRole('alert')).toHaveTextContent('Clear the current account snapshot cache?');
    expect(fetchMock.mock.calls.some(([, init]) => (init as RequestInit | undefined)?.method === 'DELETE')).toBe(false);

    fireEvent.click(screen.getByRole('button', { name: 'Confirm clear account snapshots' }));

    expect(await screen.findByText(/account snapshot data cleared/i)).toHaveTextContent(
      'Saved operation history, preferences, public market cache, and your operating-system API key were kept.',
    );
    const clearCall = fetchMock.mock.calls.find(([input]) => getFetchUrl(input as RequestInfo | URL) === '/api/account/snapshots');
    expect((clearCall?.[1] as RequestInit).method).toBe('DELETE');
  });

  it('reports a failed clear without claiming that account snapshots were removed', async () => {
    fetchMock.mockImplementation((input: RequestInfo | URL) => {
      const url = getFetchUrl(input);
      if (url === '/api/account/snapshots') {
        return Promise.resolve(failedResponse(500));
      }

      return Promise.resolve(successfulResponse(
        url === '/api/dashboard/opportunities'
          ? dashboardResponse
          : url === '/api/history/statistics'
            ? emptyHistoryStatistics
            : defaultPreferences,
      ));
    });

    render(<App />);
    await screen.findByRole('heading', { name: 'Ranked opportunities' });
    fireEvent.click(screen.getByRole('button', { name: 'Clear account snapshot data' }));
    fireEvent.click(screen.getByRole('button', { name: 'Confirm clear account snapshots' }));

    expect(await screen.findByText(/account snapshot data could not be cleared/i)).toBeVisible();
    expect(screen.queryByText(/^Account snapshot data cleared\./)).toBeNull();
  });
});

describe('personal history statistics', () => {
  it('renders evidence-scoped modeled and realized statistics with exact ratios', async () => {
    fetchMock.mockImplementation((input: RequestInfo | URL) => Promise.resolve(successfulResponse(
      getFetchUrl(input) === '/api/dashboard/opportunities'
        ? dashboardResponse
        : getFetchUrl(input) === '/api/history/statistics'
          ? populatedHistoryStatistics
          : defaultPreferences,
    )));

    render(<App />);

    const history = await screen.findByTestId('personal-history');
    expect(history).toHaveTextContent('Recorded locally from 2026-08-28T08:00:00.000Z through 2026-08-28T08:08:00.000Z.');
    expect(history).toHaveTextContent('Saved operations4 recorded');
    expect(history).toHaveTextContent('Recorded realized profit0g 0s 40c');
    expect(history).toHaveTextContent('Average recorded realized profit0g 0s 40c ÷ 2 eligible operations');
    expect(history).toHaveTextContent('Average modeled profit0g 0s 21c ÷ 2 eligible operations');
    expect(history).toHaveTextContent('Lifecycle completion rate1 completed ÷ 2 terminal operations');
    expect(history).toHaveTextContent('not guarantees');
  });

  it('explains that an empty local history does not reconstruct lifetime results', async () => {
    fetchMock.mockImplementation((input: RequestInfo | URL) => Promise.resolve(successfulResponse(
      getFetchUrl(input) === '/api/dashboard/opportunities'
        ? dashboardResponse
        : getFetchUrl(input) === '/api/history/statistics'
          ? emptyHistoryStatistics
          : defaultPreferences,
    )));

    render(<App />);

    const history = await screen.findByTestId('personal-history');
    expect(history).toHaveTextContent('No local operation history yet.');
    expect(history).toHaveTextContent('unknown lifetime history is not backfilled');
  });

  it('does not turn unavailable evidence into zero-valued metrics', async () => {
    const insufficientHistory: OperationHistoryStatistics = {
      ...emptyHistoryStatistics,
      operationCount: 1,
      firstRecordedAtUtc: '2026-08-28T08:00:00Z',
      lastRecordedAtUtc: '2026-08-28T08:01:00Z',
    };
    fetchMock.mockImplementation((input: RequestInfo | URL) => Promise.resolve(successfulResponse(
      getFetchUrl(input) === '/api/dashboard/opportunities'
        ? dashboardResponse
        : getFetchUrl(input) === '/api/history/statistics'
          ? insufficientHistory
          : defaultPreferences,
    )));

    render(<App />);

    const history = await screen.findByTestId('personal-history');
    expect(history).toHaveTextContent('No recorded sales yet.');
    expect(history).toHaveTextContent('No stored modeled financial snapshots.');
    expect(history).toHaveTextContent('No completed or cancelled operations yet.');
  });
});

async function renderReadyDashboard(): Promise<void> {
  fetchMock.mockImplementation((input: RequestInfo | URL) => Promise.resolve(successfulResponse(
    getFetchUrl(input) === '/api/dashboard/opportunities' ? dashboardResponse : defaultPreferences,
  )));
  render(<App />);
  await screen.findByRole('heading', { name: 'Ranked opportunities' });
}

function successfulResponse(payload: unknown): Response {
  return {
    ok: true,
    status: 200,
    json: async () => payload,
  } as Response;
}

function noContentResponse(): Response {
  return {
    ok: true,
    status: 204,
  } as Response;
}

function failedResponse(status: number): Response {
  return {
    ok: false,
    status,
  } as Response;
}

function getFetchUrl(input: RequestInfo | URL): string {
  return typeof input === 'string' ? input : input.toString();
}

function expectOpportunityRows(expectedLabels: string[]): void {
  expect(screen.getAllByTestId('opportunity-row')).toHaveLength(expectedLabels.length);
  expectedLabels.forEach((label) => expect(screen.getByText(label)).toBeVisible());
}
