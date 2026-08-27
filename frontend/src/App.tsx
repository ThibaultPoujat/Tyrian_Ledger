import { useEffect, useMemo, useState } from 'react';
import {
  loadDashboardOpportunities,
  loadUserSessionPreferences,
  saveUserSessionPreferences,
  type DashboardOpportunitiesResponse,
  type DashboardOpportunity,
  type UserSessionPreferences,
} from './dashboardApi';
import './App.css';

type DashboardState =
  | { kind: 'loading' }
  | { kind: 'error' }
  | { kind: 'ready'; response: DashboardOpportunitiesResponse };

interface DashboardFilters {
  freshness: 'all' | 'current' | 'stale';
}

const initialFilters: DashboardFilters = {
  freshness: 'all',
};

interface PreferenceForm {
  capitalLimitCopper: string;
  minimumProfitCopper: string;
  riskPreference: UserSessionPreferences['riskPreference'];
  strategyPreference: UserSessionPreferences['strategyPreference'];
  allocationPercent: string;
}

const initialPreferenceForm: PreferenceForm = {
  capitalLimitCopper: '',
  minimumProfitCopper: '',
  riskPreference: 'all',
  strategyPreference: 'all',
  allocationPercent: '100',
};

export default function App() {
  const [state, setState] = useState<DashboardState>({ kind: 'loading' });
  const [requestVersion, setRequestVersion] = useState(0);
  const [filters, setFilters] = useState<DashboardFilters>(initialFilters);
  const [preferences, setPreferences] = useState<PreferenceForm>(initialPreferenceForm);
  const [preferenceMessage, setPreferenceMessage] = useState<string | null>(null);
  const [isSavingPreferences, setIsSavingPreferences] = useState(false);
  const [selectedOpportunityId, setSelectedOpportunityId] = useState<number | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setState({ kind: 'loading' });

    void Promise.all([
      loadDashboardOpportunities(controller.signal),
      loadUserSessionPreferences(controller.signal),
    ])
      .then(([response, savedPreferences]) => {
        if (!controller.signal.aborted) {
          setPreferences(toPreferenceForm(savedPreferences));
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
  const filteredOpportunities = useMemo(
    () => opportunities.filter((opportunity) => matchesFilters(opportunity, filters)),
    [filters, opportunities],
  );
  const selectedOpportunity = useMemo(
    () => opportunities.find((opportunity) => opportunity.itemId === selectedOpportunityId) ?? null,
    [opportunities, selectedOpportunityId],
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
            <aside className="filter-panel" aria-labelledby="preferences-title">
              <div>
                <p className="eyebrow">Local preference profile</p>
                <h2 id="preferences-title">Opportunity preferences</h2>
              </div>

              <form
                className="filter-fields"
                noValidate
                onSubmit={(event) => {
                  event.preventDefault();
                  const { errors, value } = toUserSessionPreferences(preferences);
                  if (value === null) {
                    setPreferenceMessage(errors.join(' '));
                    return;
                  }

                  setIsSavingPreferences(true);
                  setPreferenceMessage(null);
                  void saveUserSessionPreferences(value)
                    .then((savedPreferences) => {
                      setPreferences(toPreferenceForm(savedPreferences));
                      setPreferenceMessage('Preferences saved. Updating ranked opportunities.');
                      setRequestVersion((version) => version + 1);
                    })
                    .catch(() => {
                      setPreferenceMessage('Preferences could not be saved. Your displayed results have not changed.');
                    })
                    .finally(() => setIsSavingPreferences(false));
                }}
              >
                <label htmlFor="maximum-capital">
                  Available capital
                  <span>copper</span>
                </label>
                <input
                  id="maximum-capital"
                  min="0"
                  name="capitalLimitCopper"
                  onChange={(event) => setPreferences((current) => ({
                    ...current,
                    capitalLimitCopper: event.target.value,
                  }))}
                  placeholder="No limit"
                  step="1"
                  type="number"
                  value={preferences.capitalLimitCopper}
                />

                <label htmlFor="minimum-profit">
                  Minimum modeled profit
                  <span>copper</span>
                </label>
                <input
                  id="minimum-profit"
                  min="0"
                  name="minimumProfitCopper"
                  onChange={(event) => setPreferences((current) => ({
                    ...current,
                    minimumProfitCopper: event.target.value,
                  }))}
                  placeholder="No limit"
                  step="1"
                  type="number"
                  value={preferences.minimumProfitCopper}
                />

                <label htmlFor="strategy">Strategy</label>
                <select
                  id="strategy"
                  name="strategyPreference"
                  onChange={(event) => setPreferences((current) => ({
                    ...current,
                    strategyPreference: event.target.value as PreferenceForm['strategyPreference'],
                  }))}
                  value={preferences.strategyPreference}
                >
                  <option value="all">All available strategies</option>
                  <option value="market-flip">Market flip</option>
                </select>

                <label htmlFor="confidence">Risk / confidence</label>
                <select
                  id="confidence"
                  name="riskPreference"
                  onChange={(event) => setPreferences((current) => ({
                    ...current,
                    riskPreference: event.target.value as PreferenceForm['riskPreference'],
                  }))}
                  value={preferences.riskPreference}
                >
                  <option value="all">All confidence signals</option>
                  <option value="normal">Normal confidence</option>
                  <option value="reduced">Reduced confidence</option>
                </select>

                <label htmlFor="allocation-percent">
                  Per-opportunity allocation
                  <span>percent</span>
                </label>
                <input
                  id="allocation-percent"
                  max="100"
                  min="1"
                  name="allocationPercent"
                  onChange={(event) => setPreferences((current) => ({
                    ...current,
                    allocationPercent: event.target.value,
                  }))}
                  step="1"
                  type="number"
                  value={preferences.allocationPercent}
                />

                <p className="allocation-note">
                  Each modeled opportunity can use at most this share of your available capital.
                </p>

                <button className="preferences-save" disabled={isSavingPreferences} type="submit">
                  {isSavingPreferences ? 'Saving preferences…' : 'Save and apply preferences'}
                </button>
                {preferenceMessage !== null && (
                  <p aria-live="polite" className="preference-message" role="status">
                    {preferenceMessage}
                  </p>
                )}

                <div className="freshness-filter">
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
              </form>
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
                  <p>Broaden the saved capital, profit, strategy, confidence, allocation, or freshness criteria.</p>
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
                        <th scope="col">Details</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredOpportunities.map((opportunity) => (
                        <OpportunityRow
                          key={opportunity.itemId}
                          isSelected={opportunity.itemId === selectedOpportunityId}
                          onSelect={() => setSelectedOpportunityId((current) => (
                            current === opportunity.itemId ? null : opportunity.itemId
                          ))}
                          opportunity={opportunity}
                        />
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              {selectedOpportunity !== null && (
                <OpportunityDetail
                  onClose={() => setSelectedOpportunityId(null)}
                  opportunity={selectedOpportunity}
                />
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

function OpportunityRow({
  isSelected,
  onSelect,
  opportunity,
}: {
  isSelected: boolean;
  onSelect: () => void;
  opportunity: DashboardOpportunity;
}) {
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
      <td>
        <button
          aria-expanded={isSelected}
          aria-label={`${isSelected ? 'Hide' : 'View'} details for ${opportunity.label}`}
          className="detail-toggle"
          onClick={onSelect}
          type="button"
        >
          {isSelected ? 'Hide details' : 'View details'}
        </button>
      </td>
    </tr>
  );
}

function OpportunityDetail({
  onClose,
  opportunity,
}: {
  onClose: () => void;
  opportunity: DashboardOpportunity;
}) {
  const { detail } = opportunity;

  return (
    <section
      aria-labelledby="opportunity-detail-title"
      className="opportunity-detail"
      data-testid="opportunity-detail"
    >
      <div className="opportunity-detail-heading">
        <div>
          <p className="eyebrow">Calculation detail</p>
          <h2 id="opportunity-detail-title">{opportunity.label}</h2>
        </div>
        <button className="detail-close" onClick={onClose} type="button">
          Close details
        </button>
      </div>

      <p className="scenario-disclaimer">
        <strong>Modeled scenario only.</strong> This uses the supplied order-book snapshot and configured fees.
        It is not an actual purchase, sale, fill, fee, or realized-profit outcome, and it does not guarantee one.
      </p>

      <div className="detail-grid">
        <section aria-labelledby="scenario-assumptions-title" className="detail-section">
          <h3 id="scenario-assumptions-title">Scenario assumptions</h3>
          <dl>
            <DetailTerm label="Strategy" value={formatStrategy(opportunity.strategy)} />
            <DetailTerm label="Requested quantity" value={`${detail.requestedQuantity} items`} />
            <DetailTerm label="Analyzed at" value={formatDateTime(detail.analyzedAtUtc)} />
            <DetailTerm label="Confidence / risk" value={formatConfidence(detail.confidence)} />
            <DetailTerm
              label="Acquisition assumption"
              value={`Take supplied sell levels: ${detail.acquisition.filledQuantity} of ${detail.acquisition.requestedQuantity} items for ${formatCopper(detail.acquisition.totalValueCopper)}.`}
            />
            <DetailTerm
              label="Exit assumption"
              value={`Take supplied buy levels: ${detail.exit.filledQuantity} of ${detail.exit.requestedQuantity} items for ${formatCopper(detail.exit.totalValueCopper)} gross.`}
            />
          </dl>
        </section>

        <section aria-labelledby="fees-title" className="detail-section">
          <h3 id="fees-title">Configured fees</h3>
          <dl>
            <DetailTerm
              label="Listing fee"
              value={`${formatBasisPoints(detail.fees.listingBasisPoints)} (${formatRounding(detail.fees.listingRounding)}) = ${formatCopper(detail.fees.listingFeeCopper)}`}
            />
            <DetailTerm
              label="Exchange fee"
              value={`${formatBasisPoints(detail.fees.exchangeBasisPoints)} (${formatRounding(detail.fees.exchangeRounding)}) = ${formatCopper(detail.fees.exchangeFeeCopper)}`}
            />
          </dl>
        </section>

        <section aria-labelledby="financials-title" className="detail-section">
          <h3 id="financials-title">Modeled financial result</h3>
          <dl>
            <DetailTerm label="Capital required" value={formatCopper(detail.financials.capitalRequiredCopper)} />
            <DetailTerm label="Modeled net proceeds" value={formatCopper(detail.financials.netSaleProceedsCopper)} />
            <DetailTerm label="Modeled profit" value={formatCopper(detail.financials.modeledNetProfitCopper)} />
            <DetailTerm
              label="Modeled ROI"
              value={formatBasisPoints(detail.financials.returnOnInvestmentBasisPoints)}
            />
          </dl>
        </section>

        <section aria-labelledby="liquidity-title" className="detail-section">
          <h3 id="liquidity-title">Order-book impact and liquidity</h3>
          <dl>
            <DetailTerm
              label="Acquisition liquidity"
              value={`${detail.liquidity.acquisitionFilledQuantity} of ${detail.requestedQuantity} modeled; ${formatFillStatus(detail.liquidity.isFullyAcquirable)}.`}
            />
            <DetailTerm
              label="Exit liquidity"
              value={`${detail.liquidity.liquidationFilledQuantity} of ${detail.requestedQuantity} modeled; ${formatFillStatus(detail.liquidity.isFullyLiquidatable)}.`}
            />
            <DetailTerm
              label="Acquisition price impact"
              value={formatCopper(detail.liquidity.acquisitionPriceImpactCopper)}
            />
            <DetailTerm
              label="Exit price impact"
              value={formatCopper(detail.liquidity.liquidationPriceImpactCopper)}
            />
            <DetailTerm
              label="Total price impact"
              value={formatCopper(detail.liquidity.totalPriceImpactCopper)}
            />
          </dl>
        </section>
      </div>

      <section aria-labelledby="calculation-breakdown-title" className="calculation-breakdown">
        <h3 id="calculation-breakdown-title">Human-readable calculation breakdown</h3>
        <p>
          Acquisition cost {formatCopper(detail.financials.acquisitionCostCopper)} + listing fee{' '}
          {formatCopper(detail.fees.listingFeeCopper)} = capital required{' '}
          {formatCopper(detail.financials.capitalRequiredCopper)}.
        </p>
        <p>
          Gross exit value {formatCopper(detail.financials.grossSaleValueCopper)} − listing fee{' '}
          {formatCopper(detail.fees.listingFeeCopper)} − exchange fee {formatCopper(detail.fees.exchangeFeeCopper)}
          {' '}= modeled net proceeds {formatCopper(detail.financials.netSaleProceedsCopper)}.
        </p>
        <p>
          Modeled net proceeds {formatCopper(detail.financials.netSaleProceedsCopper)} − acquisition cost{' '}
          {formatCopper(detail.financials.acquisitionCostCopper)} = modeled profit{' '}
          {formatCopper(detail.financials.modeledNetProfitCopper)} ({formatBasisPoints(detail.financials.returnOnInvestmentBasisPoints)} ROI).
        </p>
      </section>

      <section aria-labelledby="data-age-title" className="data-age-detail">
        <h3 id="data-age-title">Data age</h3>
        <p>
          <span className={`freshness-label freshness-${detail.freshness}`}>
            {formatFreshness(detail.freshness)}
          </span>{' '}
          Captured <time dateTime={detail.capturedAtUtc}>{formatDataAge(detail.capturedAtUtc)}</time>
          {' '}({formatDateTime(detail.capturedAtUtc)}); this scenario expires at {formatDateTime(detail.expiresAtUtc)}.
        </p>
      </section>
    </section>
  );
}

function DetailTerm({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function matchesFilters(opportunity: DashboardOpportunity, filters: DashboardFilters): boolean {
  return filters.freshness === 'all' || opportunity.freshness === filters.freshness;
}

function toPreferenceForm(preferences: UserSessionPreferences): PreferenceForm {
  return {
    capitalLimitCopper: preferences.capitalLimitCopper?.toString() ?? '',
    minimumProfitCopper: preferences.minimumProfitCopper?.toString() ?? '',
    riskPreference: preferences.riskPreference,
    strategyPreference: preferences.strategyPreference,
    allocationPercent: preferences.allocationPercent.toString(),
  };
}

function toUserSessionPreferences(form: PreferenceForm): {
  errors: string[];
  value: UserSessionPreferences | null;
} {
  const capitalLimitCopper = parsePreferenceCopper(form.capitalLimitCopper, 'Available capital');
  const minimumProfitCopper = parsePreferenceCopper(form.minimumProfitCopper, 'Minimum modeled profit');
  const allocationPercent = Number(form.allocationPercent);
  const errors = [capitalLimitCopper.error, minimumProfitCopper.error].filter(
    (error): error is string => error !== null,
  );

  if (!Number.isSafeInteger(allocationPercent) || allocationPercent < 1 || allocationPercent > 100) {
    errors.push('Per-opportunity allocation must be an integer from 1 through 100.');
  }

  return errors.length > 0
    ? { errors, value: null }
    : {
      errors,
      value: {
        capitalLimitCopper: capitalLimitCopper.value,
        minimumProfitCopper: minimumProfitCopper.value,
        riskPreference: form.riskPreference,
        strategyPreference: form.strategyPreference,
        allocationPercent,
      },
    };
}

function parsePreferenceCopper(value: string, label: string): { error: string | null; value: number | null } {
  if (value.trim() === '') {
    return { error: null, value: null };
  }

  const parsedValue = Number(value);
  return Number.isSafeInteger(parsedValue) && parsedValue >= 0
    ? { error: null, value: parsedValue }
    : { error: `${label} must be a non-negative whole-copper amount.`, value: null };
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

function formatDateTime(utcValue: string): string {
  const parsedDate = new Date(utcValue);
  return Number.isNaN(parsedDate.getTime()) ? 'Unknown time' : parsedDate.toISOString();
}

function formatRounding(rounding: 'down' | 'up'): string {
  return `rounded ${rounding}`;
}

function formatFillStatus(isFullyFilled: boolean): string {
  return isFullyFilled ? 'full modeled depth' : 'insufficient modeled depth';
}
