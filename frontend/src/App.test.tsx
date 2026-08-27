import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';
import type { DashboardOpportunitiesResponse, DashboardOpportunityDetail } from './dashboardApi';

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

const fetchMock = vi.fn();

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe('market dashboard filters', () => {
  it('filters opportunities by capital and minimum modeled profit', async () => {
    await renderReadyDashboard();

    fireEvent.change(screen.getByLabelText(/maximum capital/i), { target: { value: '600' } });
    expectOpportunityRows(['Sample market flip #900001']);

    fireEvent.change(screen.getByLabelText(/maximum capital/i), { target: { value: '' } });
    fireEvent.change(screen.getByLabelText(/minimum modeled profit/i), { target: { value: '600' } });
    expectOpportunityRows(['Sample market flip #900004', 'Sample market flip #900003']);
  });

  it('filters opportunities by strategy, confidence, and freshness', async () => {
    await renderReadyDashboard();

    fireEvent.change(screen.getByLabelText(/^strategy$/i), { target: { value: 'market-flip' } });
    expect(screen.queryByText('Sample craft #900005')).not.toBeInTheDocument();
    expect(screen.getAllByTestId('opportunity-row')).toHaveLength(4);

    fireEvent.change(screen.getByLabelText(/risk \/ confidence/i), { target: { value: 'reduced' } });
    expectOpportunityRows(['Sample market flip #900003']);

    fireEvent.change(screen.getByLabelText(/risk \/ confidence/i), { target: { value: 'all' } });
    fireEvent.change(screen.getByLabelText(/^freshness$/i), { target: { value: 'stale' } });
    expectOpportunityRows(['Sample market flip #900003']);
  });

  it('explains when no opportunity matches the selected filters', async () => {
    await renderReadyDashboard();

    fireEvent.change(screen.getByLabelText(/maximum capital/i), { target: { value: '1' } });

    expect(screen.getByRole('heading', { name: 'No opportunities match these filters' })).toBeVisible();
    expect(screen.queryByTestId('opportunity-row')).not.toBeInTheDocument();
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

describe('market dashboard states', () => {
  it('shows an explicit loading state before the sample feed responds', () => {
    fetchMock.mockReturnValue(new Promise(() => {}));

    render(<App />);

    expect(screen.getByRole('status')).toHaveTextContent('Loading local deterministic sample opportunities.');
  });

  it('shows an explicit error and retries the local sample feed', async () => {
    fetchMock
      .mockRejectedValueOnce(new Error('Sample feed unavailable.'))
      .mockResolvedValueOnce(successfulResponse());

    render(<App />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Dashboard data could not load');
    fireEvent.click(screen.getByRole('button', { name: 'Retry loading dashboard' }));

    expect(await screen.findByText('Sample market flip #900004')).toBeVisible();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });
});

async function renderReadyDashboard(): Promise<void> {
  fetchMock.mockResolvedValueOnce(successfulResponse());
  render(<App />);
  await screen.findByRole('heading', { name: 'Ranked opportunities' });
}

function successfulResponse(): Response {
  return {
    ok: true,
    status: 200,
    json: async () => dashboardResponse,
  } as Response;
}

function expectOpportunityRows(expectedLabels: string[]): void {
  expect(screen.getAllByTestId('opportunity-row')).toHaveLength(expectedLabels.length);
  expectedLabels.forEach((label) => expect(screen.getByText(label)).toBeVisible());
}
