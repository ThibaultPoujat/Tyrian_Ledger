import { useEffect, useMemo, useState } from 'react';
import {
  loadDashboardOpportunities,
  type DashboardOpportunitiesResponse,
  type DashboardOpportunity,
} from './dashboardApi';
import './App.css';

type DashboardState =
  | { kind: 'loading' }
  | { kind: 'error' }
  | { kind: 'ready'; response: DashboardOpportunitiesResponse };

interface DashboardFilters {
  maximumCapital: string;
  minimumProfit: string;
  strategy: string;
  confidence: 'all' | 'normal' | 'reduced';
  freshness: 'all' | 'current' | 'stale';
}

const initialFilters: DashboardFilters = {
  maximumCapital: '',
  minimumProfit: '',
  strategy: 'all',
  confidence: 'all',
  freshness: 'all',
};

export default function App() {
  const [state, setState] = useState<DashboardState>({ kind: 'loading' });
  const [requestVersion, setRequestVersion] = useState(0);
  const [filters, setFilters] = useState<DashboardFilters>(initialFilters);

  useEffect(() => {
    const controller = new AbortController();
    setState({ kind: 'loading' });

    void loadDashboardOpportunities(controller.signal)
      .then((response) => {
        if (!controller.signal.aborted) {
          setState({ kind: 'ready', response });
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setState({ kind: 'error' });
        }
      });

    return () => controller.abort();
  }, [requestVersion]);

  const opportunities = state.kind === 'ready' ? state.response.opportunities : [];
  const strategies = useMemo(
    () => Array.from(new Set(opportunities.map((opportunity) => opportunity.strategy))).sort(),
    [opportunities],
  );
  const filteredOpportunities = useMemo(
    () => opportunities.filter((opportunity) => matchesFilters(opportunity, filters)),
    [filters, opportunities],
  );

  return (
    <main className="dashboard-shell">
      <header className="dashboard-header">
        <p className="eyebrow">Tyrian Ledger · local read-only analysis</p>
        <h1>Market opportunities</h1>
        <p className="dashboard-intro">
          Ranked modeled scenarios for planning—not orders, execution predictions, or profit guarantees.
        </p>
      </header>

      {state.kind === 'loading' && (
        <section className="dashboard-state" aria-live="polite" role="status">
          <h2>Loading dashboard</h2>
          <p>Loading local deterministic sample opportunities.</p>
        </section>
      )}

      {state.kind === 'error' && (
        <section className="dashboard-state dashboard-error" role="alert">
          <h2>Dashboard data could not load</h2>
          <p>The local sample feed was unavailable. No market action has been attempted.</p>
          <button type="button" onClick={() => setRequestVersion((version) => version + 1)}>
            Retry loading dashboard
          </button>
        </section>
      )}

      {state.kind === 'ready' && (
        <>
          <section className="sample-notice" aria-label="Data source notice">
            <strong>Sample data</strong>
            <p>{state.response.sourceDescription}</p>
          </section>

          <section className="dashboard-layout" aria-label="Opportunity dashboard">
            <aside className="filter-panel" aria-labelledby="filters-title">
              <div>
                <p className="eyebrow">Refine the list</p>
                <h2 id="filters-title">Filters</h2>
              </div>

              <div className="filter-fields">
                <label htmlFor="maximum-capital">
                  Maximum capital
                  <span>copper</span>
                </label>
                <input
                  id="maximum-capital"
                  min="0"
                  name="maximumCapital"
                  onChange={(event) => setFilters((current) => ({
                    ...current,
                    maximumCapital: event.target.value,
                  }))}
                  placeholder="Any amount"
                  step="1"
                  type="number"
                  value={filters.maximumCapital}
                />

                <label htmlFor="minimum-profit">
                  Minimum modeled profit
                  <span>copper</span>
                </label>
                <input
                  id="minimum-profit"
                  min="0"
                  name="minimumProfit"
                  onChange={(event) => setFilters((current) => ({
                    ...current,
                    minimumProfit: event.target.value,
                  }))}
                  placeholder="Any amount"
                  step="1"
                  type="number"
                  value={filters.minimumProfit}
                />

                <label htmlFor="strategy">Strategy</label>
                <select
                  id="strategy"
                  name="strategy"
                  onChange={(event) => setFilters((current) => ({
                    ...current,
                    strategy: event.target.value,
                  }))}
                  value={filters.strategy}
                >
                  <option value="all">All available strategies</option>
                  {strategies.map((strategy) => (
                    <option key={strategy} value={strategy}>
                      {formatStrategy(strategy)}
                    </option>
                  ))}
                </select>

                <label htmlFor="confidence">Risk / confidence</label>
                <select
                  id="confidence"
                  name="confidence"
                  onChange={(event) => setFilters((current) => ({
                    ...current,
                    confidence: event.target.value as DashboardFilters['confidence'],
                  }))}
                  value={filters.confidence}
                >
                  <option value="all">All confidence signals</option>
                  <option value="normal">Normal confidence</option>
                  <option value="reduced">Reduced confidence</option>
                </select>

                <label htmlFor="freshness">Freshness</label>
                <select
                  id="freshness"
                  name="freshness"
                  onChange={(event) => setFilters((current) => ({
                    ...current,
                    freshness: event.target.value as DashboardFilters['freshness'],
                  }))}
                  value={filters.freshness}
                >
                  <option value="all">All data ages</option>
                  <option value="current">Current snapshot</option>
                  <option value="stale">Stale snapshot</option>
                </select>
              </div>
            </aside>

            <section className="opportunity-panel" aria-labelledby="opportunities-title">
              <div className="opportunity-panel-heading">
                <div>
                  <p className="eyebrow">Deterministic rank</p>
                  <h2 id="opportunities-title">Ranked opportunities</h2>
                </div>
                <output aria-live="polite">
                  Showing {filteredOpportunities.length} of {opportunities.length}
                </output>
              </div>

              {filteredOpportunities.length === 0 ? (
                <section className="empty-results" role="status">
                  <h3>No opportunities match these filters</h3>
                  <p>Broaden the capital, profit, strategy, confidence, or freshness criteria.</p>
                </section>
              ) : (
                <div className="opportunity-table-wrapper">
                  <table id="opportunity-results">
                    <thead>
                      <tr>
                        <th scope="col">Rank</th>
                        <th scope="col">Opportunity</th>
                        <th scope="col">Capital</th>
                        <th scope="col">Modeled profit</th>
                        <th scope="col">ROI</th>
                        <th scope="col">Liquidity proxy</th>
                        <th scope="col">Confidence / risk</th>
                        <th scope="col">Data age</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredOpportunities.map((opportunity) => (
                        <OpportunityRow key={opportunity.itemId} opportunity={opportunity} />
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              <p className="method-note">
                Liquidity is an order-book depth and price-impact proxy, not a probability of execution.
              </p>
            </section>
          </section>
        </>
      )}
    </main>
  );
}

function OpportunityRow({ opportunity }: { opportunity: DashboardOpportunity }) {
  return (
    <tr data-testid="opportunity-row">
      <td className="rank-cell">#{opportunity.rank}</td>
      <th scope="row">
        <span className="opportunity-label">{opportunity.label}</span>
        <span className="opportunity-strategy">{formatStrategy(opportunity.strategy)}</span>
      </th>
      <td>{formatCopper(opportunity.capitalRequiredCopper)}</td>
      <td className="profit-cell">{formatCopper(opportunity.modeledNetProfitCopper)}</td>
      <td>{formatBasisPoints(opportunity.returnOnInvestmentBasisPoints)}</td>
      <td>{formatLiquidity(opportunity.liquidityPriceImpactCopper)}</td>
      <td>
        <span className={`confidence-chip confidence-${opportunity.confidence}`}>
          {formatConfidence(opportunity.confidence)}
        </span>
      </td>
      <td>
        <span className={`freshness-label freshness-${opportunity.freshness}`}>
          {formatFreshness(opportunity.freshness)}
        </span>
        <time dateTime={opportunity.capturedAtUtc}>{formatDataAge(opportunity.capturedAtUtc)}</time>
      </td>
    </tr>
  );
}

function matchesFilters(opportunity: DashboardOpportunity, filters: DashboardFilters): boolean {
  const maximumCapital = parseCopper(filters.maximumCapital);
  const minimumProfit = parseCopper(filters.minimumProfit);

  return (maximumCapital === null || opportunity.capitalRequiredCopper <= maximumCapital)
    && (minimumProfit === null || opportunity.modeledNetProfitCopper >= minimumProfit)
    && (filters.strategy === 'all' || opportunity.strategy === filters.strategy)
    && (filters.confidence === 'all' || opportunity.confidence === filters.confidence)
    && (filters.freshness === 'all' || opportunity.freshness === filters.freshness);
}

function parseCopper(value: string): number | null {
  if (value.trim() === '') {
    return null;
  }

  const parsedValue = Number(value);
  return Number.isSafeInteger(parsedValue) && parsedValue >= 0 ? parsedValue : null;
}

function formatCopper(copper: number): string {
  const absoluteCopper = Math.abs(copper);
  const gold = Math.floor(absoluteCopper / 10_000);
  const silver = Math.floor((absoluteCopper % 10_000) / 100);
  const remainingCopper = absoluteCopper % 100;
  const sign = copper < 0 ? '−' : '';

  return `${sign}${gold}g ${silver}s ${remainingCopper}c`;
}

function formatBasisPoints(basisPoints: number): string {
  const absoluteBasisPoints = Math.abs(basisPoints);
  const wholePercent = Math.floor(absoluteBasisPoints / 100);
  const fractionalPercent = (absoluteBasisPoints % 100).toString().padStart(2, '0');
  const sign = basisPoints < 0 ? '−' : '';

  return `${sign}${wholePercent}.${fractionalPercent}%`;
}

function formatLiquidity(priceImpactCopper: number): string {
  return priceImpactCopper === 0
    ? 'No modeled impact'
    : `${formatCopper(priceImpactCopper)} impact`;
}

function formatConfidence(confidence: DashboardOpportunity['confidence']): string {
  return confidence === 'normal' ? 'Normal' : 'Reduced';
}

function formatFreshness(freshness: DashboardOpportunity['freshness']): string {
  return freshness === 'current' ? 'Current' : 'Stale';
}

function formatStrategy(strategy: string): string {
  return strategy
    .split('-')
    .map((word) => `${word.slice(0, 1).toUpperCase()}${word.slice(1)}`)
    .join(' ');
}

function formatDataAge(capturedAtUtc: string): string {
  const capturedAtMilliseconds = Date.parse(capturedAtUtc);
  if (Number.isNaN(capturedAtMilliseconds)) {
    return 'Unknown age';
  }

  const ageInMinutes = Math.max(0, Math.floor((Date.now() - capturedAtMilliseconds) / 60_000));
  if (ageInMinutes < 1) {
    return 'Less than 1 minute ago';
  }

  if (ageInMinutes === 1) {
    return '1 minute ago';
  }

  return `${ageInMinutes} minutes ago`;
}
