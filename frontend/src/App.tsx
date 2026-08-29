import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import {
  clearAccountSnapshotData,
  addMarketResearchWatchlistItem,
  loadAccountAccessStatus,
  loadDashboardOpportunities,
  loadMarketResearchWatchlist,
  loadOperationHistoryStatistics,
  loadUserSessionPreferences,
  saveUserSessionPreferences,
  removeMarketResearchWatchlistItem,
  type DashboardOpportunitiesResponse,
  type DashboardOpportunity,
  type FeeRounding,
  type AccountAccessStatus,
  type OperationHistoryStatistics,
  type OperationLifecycleStatistics,
  type OperationProfitStatistics,
  type MarketResearchWatchlist,
  type MarketResearchCoverage,
  type MarketResearchLiquidity,
  type MarketResearchPriceStatistics,
  type UserSessionPreferences,
} from './dashboardApi';
import {
  ExecutionDepthChart,
  HistoryLifecycleChart,
  OpportunityLandscape,
  ProfitWaterfall,
  ResearchBandChart,
  ResearchCoverageChart,
  ResearchLiquidityChart,
} from './chartComponents';
import './App.css';

type DashboardState =
  | { kind: 'loading' }
  | { kind: 'error' }
  | { kind: 'ready'; response: DashboardOpportunitiesResponse };

type AccountAccessState =
  | { kind: 'loading' }
  | { kind: 'error' }
  | { kind: 'ready'; status: AccountAccessStatus };

type HistoryStatisticsState =
  | { kind: 'loading' }
  | { kind: 'error' }
  | { kind: 'ready'; statistics: OperationHistoryStatistics };

type MarketResearchState =
  | { kind: 'loading' }
  | { kind: 'error' }
  | { kind: 'ready'; watchlist: MarketResearchWatchlist };

interface DashboardFilters {
  freshness: 'all' | 'current' | 'stale';
}

type SessionEffort = 'very-low' | 'low' | 'medium' | 'high' | 'ongoing-patient';

type OpportunityView = 'list' | 'landscape';

const initialFilters: DashboardFilters = {
  freshness: 'all',
};

interface PreferenceForm {
  capitalLimitCopper: string;
  minimumProfitCopper: string;
  riskPreference: UserSessionPreferences['riskPreference'];
  strategyPreference: UserSessionPreferences['strategyPreference'];
  allocationPercent: string;
  analysisQuantity: string;
  listingFeeBasisPoints: string;
  listingFeeRounding: '' | FeeRounding;
  exchangeFeeBasisPoints: string;
  exchangeFeeRounding: '' | FeeRounding;
}

const initialPreferenceForm: PreferenceForm = {
  capitalLimitCopper: '',
  minimumProfitCopper: '',
  riskPreference: 'all',
  strategyPreference: 'all',
  allocationPercent: '100',
  analysisQuantity: '1',
  listingFeeBasisPoints: '',
  listingFeeRounding: '',
  exchangeFeeBasisPoints: '',
  exchangeFeeRounding: '',
};

export default function App() {
  const [state, setState] = useState<DashboardState>({ kind: 'loading' });
  const [accountAccess, setAccountAccess] = useState<AccountAccessState>({ kind: 'loading' });
  const [historyStatistics, setHistoryStatistics] = useState<HistoryStatisticsState>({ kind: 'loading' });
  const [marketResearch, setMarketResearch] = useState<MarketResearchState>({ kind: 'loading' });
  const [requestVersion, setRequestVersion] = useState(0);
  const [filters, setFilters] = useState<DashboardFilters>(initialFilters);
  const [preferences, setPreferences] = useState<PreferenceForm>(initialPreferenceForm);
  const [preferenceMessage, setPreferenceMessage] = useState<string | null>(null);
  const [isSavingPreferences, setIsSavingPreferences] = useState(false);
  const [selectedOpportunityId, setSelectedOpportunityId] = useState<number | null>(null);
  const [opportunityView, setOpportunityView] = useState<OpportunityView>('list');
  const [plannedOpportunityIds, setPlannedOpportunityIds] = useState<number[]>([]);
  const [plannedEfforts, setPlannedEfforts] = useState<Record<number, SessionEffort>>({});
  const [activeSection, setActiveSection] = useState(() => window.location.hash.slice(1) || 'scan');
  const selectedOpportunityTriggerRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    const syncActiveSection = () => setActiveSection(window.location.hash.slice(1) || 'scan');
    window.addEventListener('hashchange', syncActiveSection);
    return () => window.removeEventListener('hashchange', syncActiveSection);
  }, []);

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

  useEffect(() => {
    const controller = new AbortController();
    setAccountAccess({ kind: 'loading' });

    void loadAccountAccessStatus(controller.signal)
      .then((status) => {
        if (!controller.signal.aborted) {
          setAccountAccess({ kind: 'ready', status });
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setAccountAccess({ kind: 'error' });
        }
      });

    return () => controller.abort();
  }, [requestVersion]);

  useEffect(() => {
    const controller = new AbortController();
    setMarketResearch({ kind: 'loading' });

    void loadMarketResearchWatchlist(controller.signal)
      .then((watchlist) => {
        if (!controller.signal.aborted) {
          setMarketResearch({ kind: 'ready', watchlist });
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setMarketResearch({ kind: 'error' });
        }
      });

    return () => controller.abort();
  }, [requestVersion]);

  useEffect(() => {
    const controller = new AbortController();
    setHistoryStatistics({ kind: 'loading' });

    void loadOperationHistoryStatistics(controller.signal)
      .then((statistics) => {
        if (!controller.signal.aborted) {
          setHistoryStatistics({ kind: 'ready', statistics });
        }
      })
      .catch(() => {
        if (!controller.signal.aborted) {
          setHistoryStatistics({ kind: 'error' });
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

  const addToPlan = (itemId: number) => {
    setPlannedOpportunityIds((current) => current.includes(itemId) ? current : [...current, itemId]);
    setPlannedEfforts((current) => current[itemId] ? current : { ...current, [itemId]: 'medium' });
  };

  const removeFromPlan = (itemId: number) => {
    setPlannedOpportunityIds((current) => current.filter((plannedItemId) => plannedItemId !== itemId));
    setPlannedEfforts((current) => {
      const next = { ...current };
      delete next[itemId];
      return next;
    });
  };

  return (
    <main className="dashboard-shell">
      <header className="dashboard-header">
        <div className="brand-line">
          <p className="eyebrow">Tyrian Ledger · local read-only analysis</p>
          <span className="local-badge">On this device</span>
        </div>
        <h1>Market opportunities</h1>
        <p className="dashboard-intro">
          Ranked modeled scenarios for planning—not orders, execution predictions, or profit guarantees.
        </p>
        <nav aria-label="Primary" className="primary-navigation">
          <a aria-current={activeSection === 'scan' ? 'location' : undefined} href="#scan" onClick={() => setActiveSection('scan')}>Scan</a>
          <a aria-current={activeSection === 'plan' ? 'location' : undefined} href="#plan" onClick={() => setActiveSection('plan')}>Plan <span className="nav-count">{plannedOpportunityIds.length}</span></a>
          <a aria-current={activeSection === 'history' ? 'location' : undefined} href="#history" onClick={() => setActiveSection('history')}>History</a>
          <a aria-current={activeSection === 'research' ? 'location' : undefined} href="#research" onClick={() => setActiveSection('research')}>Research</a>
          <a aria-current={activeSection === 'account' ? 'location' : undefined} href="#account" onClick={() => setActiveSection('account')}>Account &amp; settings</a>
        </nav>
      </header>

      <WorkspaceStatusBar state={state} accountAccess={accountAccess} />

      <section className="workspace-section" id="scan" aria-label="Market scan">

      {state.kind === 'loading' && (
        <section className="dashboard-state" aria-live="polite" role="status">
          <h2>Loading dashboard</h2>
          <p>Loading the bounded, read-only market scan.</p>
        </section>
      )}

      {state.kind === 'error' && (
        <section className="dashboard-state dashboard-error" role="alert">
          <h2>Dashboard data could not load</h2>
          <p>Live market data could not load. No trade or account action has been attempted.</p>
          <button type="button" onClick={() => setRequestVersion((version) => version + 1)}>
            Retry loading dashboard
          </button>
        </section>
      )}

      {state.kind === 'ready' && (
        <>
          <section className="scan-notice" aria-label="Data source notice">
            <strong>{state.response.status === 'complete' ? 'Live market data' : 'Market scan status'}</strong>
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

                <label htmlFor="analysis-quantity">
                  Analysis quantity
                  <span>items</span>
                </label>
                <input
                  id="analysis-quantity"
                  min="1"
                  name="analysisQuantity"
                  onChange={(event) => setPreferences((current) => ({
                    ...current,
                    analysisQuantity: event.target.value,
                  }))}
                  step="1"
                  type="number"
                  value={preferences.analysisQuantity}
                />

                <fieldset className="fee-settings">
                  <legend>Modeled fee rules</legend>
                  <p>Configure both rules to request detailed listings and calculate modeled profit.</p>
                  <label htmlFor="listing-fee-basis-points">
                    Listing fee
                    <span>basis points</span>
                  </label>
                  <input
                    id="listing-fee-basis-points"
                    max="10000"
                    min="0"
                    name="listingFeeBasisPoints"
                    onChange={(event) => setPreferences((current) => ({
                      ...current,
                      listingFeeBasisPoints: event.target.value,
                    }))}
                    placeholder="Unconfigured"
                    step="1"
                    type="number"
                    value={preferences.listingFeeBasisPoints}
                  />
                  <label htmlFor="listing-fee-rounding">Listing rounding</label>
                  <select
                    id="listing-fee-rounding"
                    name="listingFeeRounding"
                    onChange={(event) => setPreferences((current) => ({
                      ...current,
                      listingFeeRounding: event.target.value as PreferenceForm['listingFeeRounding'],
                    }))}
                    value={preferences.listingFeeRounding}
                  >
                    <option value="">Unconfigured</option>
                    <option value="down">Round down</option>
                    <option value="up">Round up</option>
                  </select>
                  <label htmlFor="exchange-fee-basis-points">
                    Exchange fee
                    <span>basis points</span>
                  </label>
                  <input
                    id="exchange-fee-basis-points"
                    max="10000"
                    min="0"
                    name="exchangeFeeBasisPoints"
                    onChange={(event) => setPreferences((current) => ({
                      ...current,
                      exchangeFeeBasisPoints: event.target.value,
                    }))}
                    placeholder="Unconfigured"
                    step="1"
                    type="number"
                    value={preferences.exchangeFeeBasisPoints}
                  />
                  <label htmlFor="exchange-fee-rounding">Exchange rounding</label>
                  <select
                    id="exchange-fee-rounding"
                    name="exchangeFeeRounding"
                    onChange={(event) => setPreferences((current) => ({
                      ...current,
                      exchangeFeeRounding: event.target.value as PreferenceForm['exchangeFeeRounding'],
                    }))}
                    value={preferences.exchangeFeeRounding}
                  >
                    <option value="">Unconfigured</option>
                    <option value="down">Round down</option>
                    <option value="up">Round up</option>
                  </select>
                </fieldset>

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
                  {state.response.trackedItemCount} tracked · {state.response.screenedCandidates.length} passed aggregate-price screening · {filteredOpportunities.length} ranked
                </output>
              </div>

              {state.response.status === 'fee-configuration-required' && (
                <ScreenedCandidates candidates={state.response.screenedCandidates} />
              )}

              {state.response.status === 'no-tracked-items' && (
                <section className="empty-results" role="status">
                  <h3>No tracked market items yet</h3>
                  <p>Add an item to the local research watchlist to include it in a future live scan.</p>
                </section>
              )}

              {state.response.status === 'unavailable' && (
                <section className="empty-results" role="status">
                  <h3>Live market data is unavailable</h3>
                  <p>No partial or stale fallback opportunity has been shown.</p>
                </section>
              )}

              {state.response.status === 'complete' && (
                <>
              <div className="view-toolbar" aria-label="Opportunity view">
                <span className="toolbar-label">View</span>
                <button
                  aria-pressed={opportunityView === 'list'}
                  className={opportunityView === 'list' ? 'view-button view-button-active' : 'view-button'}
                  onClick={() => setOpportunityView('list')}
                  type="button"
                >
                  List
                </button>
                <button
                  aria-pressed={opportunityView === 'landscape'}
                  className={opportunityView === 'landscape' ? 'view-button view-button-active' : 'view-button'}
                  onClick={() => setOpportunityView('landscape')}
                  type="button"
                >
                  Landscape
                </button>
                <span className="toolbar-help">List is the precise view; landscape is for orientation.</span>
                {filters.freshness !== 'all' && (
                  <div className="active-filter-chips" aria-label="Active view filters">
                    <button
                      aria-label="Remove freshness filter"
                      className="filter-chip"
                      onClick={() => setFilters(initialFilters)}
                      type="button"
                    >
                      Freshness: {filters.freshness === 'current' ? 'Current' : 'Stale'} ×
                    </button>
                  </div>
                )}
              </div>

              {opportunityView === 'landscape' && <OpportunityLandscape opportunities={filteredOpportunities} />}

              {opportunityView === 'list' && filteredOpportunities.length === 0 ? (
                <section className="empty-results" role="status">
                  <h3>No opportunities match these filters</h3>
                  <p>Broaden the saved capital, profit, strategy, confidence, allocation, or freshness criteria.</p>
                </section>
              ) : opportunityView === 'list' ? (
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
                        <th scope="col">Plan</th>
                        <th scope="col">Details</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredOpportunities.map((opportunity) => (
                        <OpportunityRow
                          key={opportunity.itemId}
                          isSelected={opportunity.itemId === selectedOpportunityId}
                          onSelect={(trigger) => {
                            if (selectedOpportunityId === opportunity.itemId) {
                              setSelectedOpportunityId(null);
                              requestAnimationFrame(() => trigger.focus());
                              return;
                            }

                            selectedOpportunityTriggerRef.current = trigger;
                            setSelectedOpportunityId(opportunity.itemId);
                          }}
                          isPlanned={plannedOpportunityIds.includes(opportunity.itemId)}
                          onTogglePlan={() => plannedOpportunityIds.includes(opportunity.itemId)
                            ? removeFromPlan(opportunity.itemId)
                            : addToPlan(opportunity.itemId)}
                          opportunity={opportunity}
                        />
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : null}
                </>
              )}

              {selectedOpportunity !== null && (
                <OpportunityDetail
                  onClose={() => {
                    const trigger = selectedOpportunityTriggerRef.current;
                    setSelectedOpportunityId(null);
                    requestAnimationFrame(() => trigger?.focus());
                  }}
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
      </section>

      <section className="workspace-section" id="plan" aria-labelledby="plan-title">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Session planning</p>
            <h2 id="plan-title">Build a practical shortlist</h2>
          </div>
          <p>Choose what fits today. Effort labels are approximations, not execution-time promises.</p>
        </div>
        <SessionPlanPanel
          opportunities={opportunities}
          plannedEfforts={plannedEfforts}
          plannedOpportunityIds={plannedOpportunityIds}
          onEffortChange={(itemId, effort) => setPlannedEfforts((current) => ({ ...current, [itemId]: effort }))}
          onRemove={removeFromPlan}
          capitalLimitCopper={parsePreferenceCopper(preferences.capitalLimitCopper, 'Available capital').value}
        />
      </section>

      <section className="workspace-section" id="history" aria-labelledby="history-section-title">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Local history</p>
            <h2 id="history-section-title">Personal history</h2>
          </div>
          <p>Recorded outcomes are separate from saved scenarios.</p>
        </div>
        <OperationHistoryPanel historyStatistics={historyStatistics} />
      </section>

      <section className="workspace-section" id="research" aria-labelledby="research-section-title">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Local observations</p>
            <h2 id="research-section-title">Market research</h2>
          </div>
          <p>Descriptive evidence from this device—not forecasts or investment advice.</p>
        </div>
        <MarketResearchPanel
          marketResearch={marketResearch}
          onRefresh={() => setRequestVersion((version) => version + 1)}
        />
      </section>

      <section className="workspace-section" id="account" aria-labelledby="account-section-title">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Account &amp; settings</p>
            <h2 id="account-section-title">Optional account context</h2>
          </div>
          <p>Market scanning works without an API key. Account data stays local.</p>
        </div>
        <AccountAccessPanel accountAccess={accountAccess} />
        <CraftingReadinessPanel accountAccess={accountAccess} />
        <LocalAccountDataPanel />
      </section>
    </main>
  );
}

function WorkspaceStatusBar({ state, accountAccess }: { state: DashboardState; accountAccess: AccountAccessState }) {
  const marketStatus = state.kind === 'loading'
    ? 'Refreshing'
    : state.kind === 'error'
      ? 'Unavailable'
      : state.response.status === 'complete'
        ? 'Current scan'
        : state.response.status === 'fee-configuration-required'
          ? 'Fee setup needed'
          : state.response.status === 'no-tracked-items'
            ? 'No tracked items'
            : 'Unavailable';
  const lastRefresh = state.kind === 'ready' ? formatDateTime(state.response.generatedAtUtc) : state.kind === 'loading' ? 'Waiting for scan' : 'Unavailable';
  const accountStatus = accountAccess.kind === 'loading'
    ? 'Checking'
    : accountAccess.kind === 'ready' && accountAccess.status.validationStatus === 'valid'
      ? 'Connected'
      : 'Optional';

  return (
    <section className="workspace-status-bar" aria-label="Workspace status">
      <div className="workspace-status-item">
        <span>Market snapshot</span>
        <strong>{marketStatus}</strong>
      </div>
      <div className="workspace-status-item">
        <span>Last refresh</span>
        <strong>{lastRefresh}</strong>
      </div>
      <div className="workspace-status-item">
        <span>Account context</span>
        <strong>{accountStatus}</strong>
      </div>
      <div className="workspace-status-item">
        <span>Storage</span>
        <strong>On this device</strong>
      </div>
    </section>
  );
}

function SessionPlanPanel({
  opportunities,
  plannedEfforts,
  plannedOpportunityIds,
  onEffortChange,
  onRemove,
  capitalLimitCopper,
}: {
  opportunities: DashboardOpportunity[];
  plannedEfforts: Record<number, SessionEffort>;
  plannedOpportunityIds: number[];
  onEffortChange: (itemId: number, effort: SessionEffort) => void;
  onRemove: (itemId: number) => void;
  capitalLimitCopper: number | null;
}) {
  const [effortFilter, setEffortFilter] = useState<'all' | SessionEffort>('all');
  const plannedOpportunities = plannedOpportunityIds
    .map((itemId) => opportunities.find((opportunity) => opportunity.itemId === itemId))
    .filter((opportunity): opportunity is DashboardOpportunity => opportunity !== undefined);
  const visibleOpportunities = plannedOpportunities.filter((opportunity) => effortFilter === 'all' || plannedEfforts[opportunity.itemId] === effortFilter);
  const totalCapitalCopper = plannedOpportunities.reduce((total, opportunity) => total + opportunity.capitalRequiredCopper, 0);

  return (
    <section className="plan-panel" data-testid="session-plan" aria-label="Session shortlist">
      <div className="plan-controls">
        <label htmlFor="plan-effort-filter">Effort preference</label>
        <select
          id="plan-effort-filter"
          value={effortFilter}
          onChange={(event) => setEffortFilter(event.target.value as 'all' | SessionEffort)}
        >
          <option value="all">All effort labels</option>
          {effortOptions.map((effort) => <option key={effort.value} value={effort.value}>{effort.label}</option>)}
        </select>
        <p>These are your planning labels, not time estimates.</p>
      </div>

      <div className="plan-budget" aria-label="Planned capital allocation">
        <div className="plan-budget-heading">
          <span>Planned capital</span>
          <strong>{formatCopper(totalCapitalCopper)}{capitalLimitCopper === null ? ' · no limit set' : ` of ${formatCopper(capitalLimitCopper)}`}</strong>
        </div>
        <div
          aria-label={capitalLimitCopper === null
            ? 'Capital allocation is not comparable until a capital limit is set.'
            : `${formatCopper(totalCapitalCopper)} of ${formatCopper(capitalLimitCopper)} planned capital.`}
          className={capitalLimitCopper !== null && totalCapitalCopper > capitalLimitCopper ? 'allocation-track allocation-track-over' : 'allocation-track'}
          role="img"
        >
          {capitalLimitCopper !== null && capitalLimitCopper > 0 && plannedOpportunities.map((opportunity, index) => (
            <span
              className={`allocation-segment allocation-segment-${index % 4}`}
              key={opportunity.itemId}
              style={{ width: `${(opportunity.capitalRequiredCopper / capitalLimitCopper) * 100}%` }}
              title={`${opportunity.label}: ${formatCopper(opportunity.capitalRequiredCopper)}`}
            />
          ))}
        </div>
        <p>{capitalLimitCopper === null
          ? 'Set available capital in Scan to compare this shortlist with a budget.'
          : totalCapitalCopper > capitalLimitCopper
            ? `${formatCopper(totalCapitalCopper - capitalLimitCopper)} over the configured budget.`
            : `${formatCopper(capitalLimitCopper - totalCapitalCopper)} remaining before the configured budget.`}</p>
      </div>

      {plannedOpportunities.length === 0 ? (
        <div className="plan-empty">
          <h3>Your shortlist is empty</h3>
          <p>Add opportunities from the Scan view. They will stay selected while you review their details.</p>
          <a className="secondary-action" href="#scan">Browse the market scan</a>
        </div>
      ) : visibleOpportunities.length === 0 ? (
        <div className="plan-empty">
          <h3>No items match this effort label</h3>
          <p>Choose another label or update the effort label on an item in your shortlist.</p>
        </div>
      ) : (
        <div className="plan-list">
          {visibleOpportunities.map((opportunity, index) => (
            <article className="plan-item" key={opportunity.itemId}>
              <div className="plan-item-rank">{index + 1}</div>
              <div className="plan-item-main">
                <h3>{opportunity.label}</h3>
                <p>{formatCopper(opportunity.modeledNetProfitCopper)} modeled profit · {formatCopper(opportunity.capitalRequiredCopper)} capital · {formatConfidence(opportunity.confidence)} confidence</p>
              </div>
              <label className="plan-effort-label" htmlFor={`effort-${opportunity.itemId}`}>
                Effort label
                <select id={`effort-${opportunity.itemId}`} value={plannedEfforts[opportunity.itemId] ?? 'medium'} onChange={(event) => onEffortChange(opportunity.itemId, event.target.value as SessionEffort)}>
                  {effortOptions.map((effort) => <option key={effort.value} value={effort.value}>{effort.label}</option>)}
                </select>
              </label>
              <button className="plan-remove" onClick={() => onRemove(opportunity.itemId)} type="button">Remove from plan</button>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}

const effortOptions: Array<{ value: SessionEffort; label: string }> = [
  { value: 'very-low', label: 'Very low' },
  { value: 'low', label: 'Low' },
  { value: 'medium', label: 'Medium' },
  { value: 'high', label: 'High' },
  { value: 'ongoing-patient', label: 'Ongoing / patient' },
];

function CraftingReadinessPanel({ accountAccess }: { accountAccess: AccountAccessState }) {
  const craftingEnabled = accountAccess.kind === 'ready'
    && accountAccess.status.validationStatus === 'valid'
    && accountAccess.status.features.some((feature) => feature.feature === 'account-crafting' && feature.isAvailable);

  return (
    <section className="crafting-panel" aria-labelledby="crafting-title" data-testid="crafting-readiness">
      <div className="crafting-panel-heading">
        <div>
          <p className="eyebrow">Account-aware analysis</p>
          <h2 id="crafting-title">Crafting analysis</h2>
        </div>
        <span className={`status-pill ${craftingEnabled ? 'status-ready' : 'status-pending'}`}>{craftingEnabled ? 'Access ready' : 'Needs account access'}</span>
      </div>
      {craftingEnabled ? (
        <p>Account crafting access is ready. Recipe paths and owned-material comparisons will appear when a snapshot is available.</p>
      ) : (
        <p>Connect an API key with character and recipe permissions to compare buy-all, use-owned, and mixed crafting paths. Market scanning remains available without it.</p>
      )}
      <div className="crafting-preview-grid" aria-label="Crafting analysis preview">
        <div><strong>Recipe path</strong><span>Output → recipe → ingredients</span></div>
        <div><strong>Cost comparison</strong><span>Buy-all · owned · mixed</span></div>
        <div><strong>Economic status</strong><span>Owned materials are not free</span></div>
      </div>
    </section>
  );
}

function AccountAccessPanel({ accountAccess }: { accountAccess: AccountAccessState }) {
  if (accountAccess.kind === 'loading') {
    return (
      <section className="account-access" aria-live="polite" aria-label="Account access status">
        <strong>Account access</strong>
        <p>Checking the locally configured API key.</p>
      </section>
    );
  }

  if (accountAccess.kind === 'error') {
    return (
      <section className="account-access account-access-error" aria-label="Account access status" role="status">
        <strong>Account access unavailable</strong>
        <p>Account-aware features remain disabled. Market analysis is still available.</p>
      </section>
    );
  }

  const { status } = accountAccess;
  if (status.validationStatus !== 'valid') {
    const message = status.validationStatus === 'notconfigured'
      ? 'No locally configured API key was found.'
      : status.validationStatus === 'invalid'
        ? 'The locally configured API key could not be validated.'
        : 'The configured API key could not be checked right now.';

    return (
      <section className="account-access account-access-error" aria-label="Account access status" role="status">
        <strong>Account-aware features disabled</strong>
        <p>{message}</p>
      </section>
    );
  }

  return (
    <section className="account-access" aria-label="Account access status">
      <div>
        <strong>Account access verified</strong>
        {status.keyName !== null && <p>Key name: <output>{status.keyName}</output></p>}
        <p>Granted permissions: {status.permissions.join(', ') || 'none'}.</p>
      </div>
      <ul>
        {status.features.map((feature) => (
          <li key={feature.feature}>
            {feature.isAvailable
              ? `${formatAccountFeature(feature.feature)} enabled`
              : `${formatAccountFeature(feature.feature)} disabled — missing ${feature.missingPermissions.join(', ')}.`}
          </li>
        ))}
      </ul>
    </section>
  );
}

function formatAccountFeature(feature: AccountAccessStatus['features'][number]['feature']): string {
  return feature === 'account-materials' ? 'Account materials' : 'Account crafting';
}

function LocalAccountDataPanel() {
  const [isConfirmationVisible, setIsConfirmationVisible] = useState(false);
  const [isClearing, setIsClearing] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const clearTriggerRef = useRef<HTMLButtonElement | null>(null);
  const confirmClearRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    if (isConfirmationVisible) {
      confirmClearRef.current?.focus();
    }
  }, [isConfirmationVisible]);

  const clearSnapshots = () => {
    setIsClearing(true);
    setMessage(null);
    void clearAccountSnapshotData()
      .then(() => {
        setIsConfirmationVisible(false);
        setMessage('Account snapshot data cleared. Saved operation history, preferences, public market cache, and your operating-system API key were kept.');
        requestAnimationFrame(() => clearTriggerRef.current?.focus());
      })
      .catch(() => {
        setMessage('Account snapshot data could not be cleared. Your existing account snapshot cache may still be available.');
      })
      .finally(() => setIsClearing(false));
  };

  return (
    <section className="local-data-panel" aria-labelledby="local-data-title">
      <p className="eyebrow">Local data</p>
      <h2 id="local-data-title">Account snapshot data</h2>
      <p>
        Account snapshots are minimized data kept only in this application session. Clearing them stays on this device and never uploads data.
      </p>
      <p>
        Saved operation history, preferences, public market cache, and your operating-system API key are not removed.
      </p>

      {!isConfirmationVisible && (
        <button
          className="local-data-clear"
          onClick={() => {
            setMessage(null);
            setIsConfirmationVisible(true);
          }}
          ref={clearTriggerRef}
          type="button"
        >
          Clear account snapshot data
        </button>
      )}

      {isConfirmationVisible && (
        <div className="local-data-confirmation" role="alert">
          <p>
            Clear the current account snapshot cache? Future account analysis will fetch fresh data when needed.
          </p>
          <div className="local-data-actions">
            <button className="local-data-clear" disabled={isClearing} onClick={clearSnapshots} ref={confirmClearRef} type="button">
              {isClearing ? 'Clearing account snapshots…' : 'Confirm clear account snapshots'}
            </button>
            <button
              className="local-data-cancel"
              disabled={isClearing}
              onClick={() => {
                setIsConfirmationVisible(false);
                requestAnimationFrame(() => clearTriggerRef.current?.focus());
              }}
              type="button"
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {message !== null && (
        <p aria-live="polite" className="local-data-message" role="status">
          {message}
        </p>
      )}
    </section>
  );
}

function MarketResearchPanel({
  marketResearch,
  onRefresh,
}: {
  marketResearch: MarketResearchState;
  onRefresh: () => void;
}) {
  const [itemId, setItemId] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  if (marketResearch.kind === 'loading') {
    return (
      <section className="research-panel" aria-label="Historical market research" aria-live="polite">
        <p className="eyebrow">Local observations</p>
        <h2>Historical market research</h2>
        <p>Loading your local watchlist observations.</p>
      </section>
    );
  }

  if (marketResearch.kind === 'error') {
    return (
      <section className="research-panel research-panel-error" aria-label="Historical market research" role="status">
        <p className="eyebrow">Local observations</p>
        <h2>Historical market research is unavailable</h2>
        <p>Local observations could not load. No market action has been attempted.</p>
        <button onClick={onRefresh} type="button">Retry research</button>
      </section>
    );
  }

  const { watchlist } = marketResearch;
  const addItem = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const parsedItemId = Number(itemId);
    if (!Number.isSafeInteger(parsedItemId) || parsedItemId <= 0) {
      setMessage('Enter a positive whole Guild Wars 2 item ID.');
      return;
    }

    setIsSaving(true);
    setMessage(null);
    void addMarketResearchWatchlistItem(parsedItemId)
      .then(() => {
        setItemId('');
        setMessage(`Item #${parsedItemId} is now collected locally for research.`);
        onRefresh();
      })
      .catch(() => setMessage('That item could not be added. It may already be tracked or the local watchlist may be full.'))
      .finally(() => setIsSaving(false));
  };
  const removeItem = (removedItemId: number) => {
    setIsSaving(true);
    setMessage(null);
    void removeMarketResearchWatchlistItem(removedItemId)
      .then(() => {
        setMessage(`Item #${removedItemId} was removed from local research collection.`);
        onRefresh();
      })
      .catch(() => setMessage('That item could not be removed from the local research watchlist.'))
      .finally(() => setIsSaving(false));
  };

  return (
    <section className="research-panel" aria-labelledby="research-title" data-testid="market-research">
      <div className="research-panel-heading">
        <div>
          <p className="eyebrow">Local observations</p>
          <h2 id="research-title">Historical market research</h2>
        </div>
        <output>{watchlist.items.length} research items; {watchlist.trackedItemCount} of {watchlist.maximumTrackedItemCount} tracked</output>
      </div>
      <p className="research-note">
        Observed local prices and liquidity are descriptive evidence, not investment advice, a forecast, or a guarantee.
      </p>

      <form className="research-add-form" noValidate onSubmit={addItem}>
        <label htmlFor="research-item-id">Guild Wars 2 item ID</label>
        <input
          id="research-item-id"
          inputMode="numeric"
          min="1"
          onChange={(event) => setItemId(event.target.value)}
          placeholder="e.g. 19721"
          step="1"
          type="number"
          value={itemId}
        />
        <button disabled={isSaving} type="submit">Add to research watchlist</button>
      </form>
      {message !== null && <p aria-live="polite" className="research-message" role="status">{message}</p>}

      <div className="research-chart-grid">
        <ResearchBandChart items={watchlist.items} />
        <ResearchLiquidityChart items={watchlist.items} />
        <ResearchCoverageChart items={watchlist.items} />
      </div>

      {watchlist.items.length === 0 ? (
        <p className="research-empty">No local research items yet. Add an item ID to begin collecting local observations.</p>
      ) : (
        <div className="research-table-wrapper">
          <table className="research-table">
            <thead>
              <tr>
                <th scope="col">Item</th>
                <th scope="col">Local coverage</th>
                <th scope="col">Observed buy band</th>
                <th scope="col">Observed sell band</th>
                <th scope="col">Liquidity variability</th>
                <th scope="col">Watchlist</th>
              </tr>
            </thead>
            <tbody>
              {watchlist.items.map((item) => (
                <tr data-testid="research-row" key={item.itemId}>
                  <th scope="row">Item #{item.itemId}</th>
                  <td>{formatCoverage(item.coverage)}</td>
                  <td>{formatObservedBand(item.buyPrices)}</td>
                  <td>{formatObservedBand(item.sellPrices)}</td>
                  <td>{formatLiquidityVariability(item.buyLiquidity, item.sellLiquidity)}</td>
                  <td>
                    <button
                      className="research-remove"
                      disabled={isSaving}
                      onClick={() => removeItem(item.itemId)}
                      type="button"
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <p className="research-note research-note-footer">
        Percentiles and liquidity estimates appear only after enough local observations exist; missing values are not treated as zero.
      </p>
    </section>
  );
}

function OperationHistoryPanel({ historyStatistics }: { historyStatistics: HistoryStatisticsState }) {
  if (historyStatistics.kind === 'loading') {
    return (
      <section className="history-panel" aria-live="polite" aria-label="Personal history">
        <p className="eyebrow">Local history</p>
        <h2>Personal history</h2>
        <p>Loading locally recorded operation statistics.</p>
      </section>
    );
  }

  if (historyStatistics.kind === 'error') {
    return (
      <section className="history-panel history-panel-error" aria-label="Personal history" role="status">
        <p className="eyebrow">Local history</p>
        <h2>Personal history is unavailable</h2>
        <p>Statistics could not load. No local history has been changed.</p>
      </section>
    );
  }

  const { statistics } = historyStatistics;
  if (statistics.operationCount === 0) {
    return (
      <section className="history-panel" aria-labelledby="history-title" data-testid="personal-history">
        <p className="eyebrow">Local history</p>
        <h2 id="history-title">Personal history</h2>
        <p>No local operation history yet.</p>
        <p className="history-note">
          Statistics start with operations recorded here; unknown lifetime history is not backfilled.
        </p>
      </section>
    );
  }

  return (
    <section className="history-panel" aria-labelledby="history-title" data-testid="personal-history">
      <div className="history-panel-heading">
        <div>
          <p className="eyebrow">Local history</p>
          <h2 id="history-title">Personal history</h2>
        </div>
        <p className="history-coverage">
          Recorded locally from <time dateTime={statistics.firstRecordedAtUtc ?? undefined}>
            {formatDateTime(statistics.firstRecordedAtUtc ?? '')}
          </time>{' '}
          through <time dateTime={statistics.lastRecordedAtUtc ?? undefined}>
            {formatDateTime(statistics.lastRecordedAtUtc ?? '')}
          </time>.
        </p>
      </div>

      <dl className="history-statistics">
        <HistoryStatistic label="Saved operations" value={`${statistics.operationCount} recorded`} />
        <HistoryStatistic
          label="Recorded realized profit"
          value={formatProfitTotal(statistics.realizedProfit, 'No recorded sales yet.')}
        />
        <HistoryStatistic
          label="Average recorded realized profit"
          value={formatExactProfitRatio(statistics.realizedProfit, 'No recorded sales yet.')}
        />
        <HistoryStatistic
          label="Average modeled profit"
          value={formatExactProfitRatio(statistics.modeledNetProfit, 'No stored modeled financial snapshots.')}
        />
        <HistoryStatistic
          label="Lifecycle completion rate"
          value={formatCompletionRatio(statistics.lifecycle)}
        />
      </dl>

      <HistoryLifecycleChart statistics={statistics} />

      <p className="history-note">
        Realized values use recorded acquisition, sale, and fee evidence. Modeled values remain saved scenarios,
        not guarantees. Averages and completion use exact ratios so no copper is rounded away.
      </p>
    </section>
  );
}

function HistoryStatistic({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function formatProfitTotal(statistics: OperationProfitStatistics, unavailableMessage: string): string {
  return statistics.totalCopper === null ? unavailableMessage : formatCopper(statistics.totalCopper);
}

function formatExactProfitRatio(statistics: OperationProfitStatistics, unavailableMessage: string): string {
  if (statistics.totalCopper === null) {
    return unavailableMessage;
  }

  const operationLabel = statistics.eligibleOperationCount === 1 ? 'operation' : 'operations';
  return `${formatCopper(statistics.totalCopper)} ÷ ${statistics.eligibleOperationCount} eligible ${operationLabel}`;
}

function formatCoverage(coverage: MarketResearchCoverage): string {
  const sampleLabel = coverage.observationCount === 1 ? 'sample' : 'samples';
  if (coverage.observationCount === 0) {
    return 'No local observations yet.';
  }

  return `${coverage.observationCount} local ${sampleLabel}: ${formatDateTime(coverage.firstCapturedAtUtc ?? '')} through ${formatDateTime(coverage.lastCapturedAtUtc ?? '')}.`;
}

function formatObservedBand(statistics: MarketResearchPriceStatistics): string {
  if (statistics.tenthPercentileCopper === null
    || statistics.medianCopper === null
    || statistics.ninetiethPercentileCopper === null) {
    return `Insufficient local sample (${statistics.observationCount} observed).`;
  }

  return `P10 ${formatCopper(statistics.tenthPercentileCopper)} · median ${formatCopper(statistics.medianCopper)} · P90 ${formatCopper(statistics.ninetiethPercentileCopper)}`;
}

function formatLiquidityVariability(
  buyLiquidity: MarketResearchLiquidity,
  sellLiquidity: MarketResearchLiquidity,
): string {
  if (buyLiquidity.coefficientOfVariationPercent === null || sellLiquidity.coefficientOfVariationPercent === null) {
    return 'Insufficient local sample.';
  }

  return `Buy ${formatPercentage(buyLiquidity.coefficientOfVariationPercent)} · sell ${formatPercentage(sellLiquidity.coefficientOfVariationPercent)}`;
}

function formatCompletionRatio(lifecycle: OperationLifecycleStatistics): string {
  if (lifecycle.terminalOperationCount === 0) {
    return 'No completed or cancelled operations yet.';
  }

  const operationLabel = lifecycle.terminalOperationCount === 1 ? 'operation' : 'operations';
  return `${lifecycle.completedOperationCount} completed ÷ ${lifecycle.terminalOperationCount} terminal ${operationLabel}`;
}

function ScreenedCandidates({
  candidates,
}: {
  candidates: DashboardOpportunitiesResponse['screenedCandidates'];
}) {
  return (
    <section className="empty-results" data-testid="screened-candidates" role="status">
      <h3>Fee configuration required</h3>
      <p>
        {candidates.length === 0
          ? 'No tracked item passed the aggregate-price screen, so no detailed listings were requested.'
          : `${candidates.length} tracked item${candidates.length === 1 ? '' : 's'} passed the aggregate-price screen. Configure both fee rules to request detailed listings and model profit.`}
      </p>
      {candidates.length > 0 && (
        <ul>
          {candidates.map((candidate) => (
            <li key={candidate.itemId}>
              Item #{candidate.itemId}: best bid {formatCopper(candidate.bestBidCopper)} · best ask {formatCopper(candidate.bestAskCopper)}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

function OpportunityRow({
  isSelected,
  isPlanned,
  onSelect,
  onTogglePlan,
  opportunity,
}: {
  isSelected: boolean;
  isPlanned: boolean;
  onSelect: (trigger: HTMLButtonElement) => void;
  onTogglePlan: () => void;
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
          aria-pressed={isPlanned}
          aria-label={`${isPlanned ? 'Remove' : 'Add'} ${opportunity.label} ${isPlanned ? 'from' : 'to'} session plan`}
          className={isPlanned ? 'plan-toggle plan-toggle-active' : 'plan-toggle'}
          onClick={onTogglePlan}
          type="button"
        >
          {isPlanned ? 'Planned' : 'Add'}
        </button>
      </td>
      <td>
        <button
          aria-expanded={isSelected}
          aria-label={`${isSelected ? 'Hide' : 'View'} details for ${opportunity.label}`}
          className="detail-toggle"
          onClick={(event) => onSelect(event.currentTarget)}
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
  const closeButtonRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    closeButtonRef.current?.focus();
  }, [opportunity.itemId]);

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
        <button className="detail-close" onClick={onClose} ref={closeButtonRef} type="button">
          Close details
        </button>
      </div>

      <p className="scenario-disclaimer">
        <strong>Modeled scenario only.</strong> This uses the supplied order-book snapshot and configured fees.
        It is not an actual purchase, sale, fill, fee, or realized-profit outcome, and it does not guarantee one.
      </p>

      <div className="detail-summary-grid" aria-label="Modeled opportunity summary">
        <MetricCard label="Modeled profit" value={formatCopper(detail.financials.modeledNetProfitCopper)} note="Scenario result" />
        <MetricCard label="Capital required" value={formatCopper(detail.financials.capitalRequiredCopper)} note="Modeled input" />
        <MetricCard label="Modeled ROI" value={formatBasisPoints(detail.financials.returnOnInvestmentBasisPoints)} note="Based on modeled capital" />
        <MetricCard label="Liquidity proxy" value={formatLiquidity(detail.liquidity.totalPriceImpactCopper)} note="Price impact, not execution odds" />
        <MetricCard label="Data age" value={formatFreshness(detail.freshness)} note={formatDataAge(detail.capturedAtUtc)} />
      </div>

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

      <div className="detail-chart-grid">
        <ExecutionDepthChart acquisition={detail.acquisition} exit={detail.exit} />
        <ProfitWaterfall
          exchangeFeeCopper={detail.fees.exchangeFeeCopper}
          financials={detail.financials}
          listingFeeCopper={detail.fees.listingFeeCopper}
        />
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

function MetricCard({ label, value, note }: { label: string; value: string; note: string }) {
  return (
    <div className="detail-summary-card">
      <span>{label}</span>
      <strong>{value}</strong>
      <p>{note}</p>
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
    analysisQuantity: preferences.analysisQuantity.toString(),
    listingFeeBasisPoints: preferences.listingFeeBasisPoints?.toString() ?? '',
    listingFeeRounding: preferences.listingFeeRounding ?? '',
    exchangeFeeBasisPoints: preferences.exchangeFeeBasisPoints?.toString() ?? '',
    exchangeFeeRounding: preferences.exchangeFeeRounding ?? '',
  };
}

function toUserSessionPreferences(form: PreferenceForm): {
  errors: string[];
  value: UserSessionPreferences | null;
} {
  const capitalLimitCopper = parsePreferenceCopper(form.capitalLimitCopper, 'Available capital');
  const minimumProfitCopper = parsePreferenceCopper(form.minimumProfitCopper, 'Minimum modeled profit');
  const allocationPercent = Number(form.allocationPercent);
  const analysisQuantity = Number(form.analysisQuantity);
  const listingFeeBasisPoints = parseFeeBasisPoints(form.listingFeeBasisPoints, 'Listing fee');
  const exchangeFeeBasisPoints = parseFeeBasisPoints(form.exchangeFeeBasisPoints, 'Exchange fee');
  const errors = [capitalLimitCopper.error, minimumProfitCopper.error].filter(
    (error): error is string => error !== null,
  );

  if (!Number.isSafeInteger(allocationPercent) || allocationPercent < 1 || allocationPercent > 100) {
    errors.push('Per-opportunity allocation must be an integer from 1 through 100.');
  }

  if (!Number.isSafeInteger(analysisQuantity) || analysisQuantity <= 0) {
    errors.push('Analysis quantity must be a positive whole number.');
  }

  if (listingFeeBasisPoints.error !== null) {
    errors.push(listingFeeBasisPoints.error);
  }

  if (exchangeFeeBasisPoints.error !== null) {
    errors.push(exchangeFeeBasisPoints.error);
  }

  const feeValues = [
    listingFeeBasisPoints.value,
    form.listingFeeRounding || null,
    exchangeFeeBasisPoints.value,
    form.exchangeFeeRounding || null,
  ];
  if (!feeValues.every((value) => value === null) && !feeValues.every((value) => value !== null)) {
    errors.push('Listing and exchange fee rules must both be fully configured or both be blank.');
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
        analysisQuantity,
        listingFeeBasisPoints: listingFeeBasisPoints.value,
        listingFeeRounding: form.listingFeeRounding || null,
        exchangeFeeBasisPoints: exchangeFeeBasisPoints.value,
        exchangeFeeRounding: form.exchangeFeeRounding || null,
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

function parseFeeBasisPoints(value: string, label: string): { error: string | null; value: number | null } {
  if (value.trim() === '') {
    return { error: null, value: null };
  }

  const parsedValue = Number(value);
  return Number.isSafeInteger(parsedValue) && parsedValue >= 0 && parsedValue <= 10_000
    ? { error: null, value: parsedValue }
    : { error: `${label} must be an integer from 0 through 10000 basis points.`, value: null };
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

function formatPercentage(value: number): string {
  return `${value.toFixed(2)}%`;
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
