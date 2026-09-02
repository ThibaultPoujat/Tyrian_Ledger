import { type FormEvent, type KeyboardEvent, type MouseEvent, useEffect, useMemo, useRef, useState } from 'react';
import './App.css';
import {
  clearSettings,
  formatCopper,
  formatModeledRoi,
  formatSnapshotTime,
  loadSettings,
  saveSettings,
  type CapitalInput,
  type RiskProfile,
  type ValidatedM9Settings,
  validateSettings,
} from './m9';
import {
  calculateSnapshotRecommendations,
  type SnapshotRecommendation,
  type SnapshotRecommendationResult,
} from './marketSnapshot';
import {
  classifySnapshotFreshness,
  formatSnapshotAge,
  loadStaticMarketSnapshot,
  millisecondsUntilSnapshotExpiry,
  resolveMarketSnapshotUrl,
  type SnapshotFreshness,
  type StaticSnapshotLoadState,
} from './staticSnapshot';

type StaticRoute = 'recommendations' | 'settings';

const profileDetails: Record<RiskProfile, { name: string; spend: string; roi: string; profit: string }> = {
  cautious: { name: 'Cautious', spend: '10% of capital', roi: '5%', profit: '10 silver' },
  balanced: { name: 'Balanced', spend: '25% of capital', roi: '8%', profit: '25 silver' },
  adventurous: { name: 'Adventurous', spend: '50% of capital', roi: '12%', profit: '50 silver' },
};

function getRoute(hash: string): StaticRoute | null {
  const routePath = hash === '' ? '/' : hash.startsWith('#') ? hash.slice(1) : null;
  switch (routePath) {
    case '/':
    case '/recommendations':
      return 'recommendations';
    case '/settings':
      return 'settings';
    default:
      return null;
  }
}

function navigationTarget(route: StaticRoute): string {
  return route === 'recommendations' ? '#/recommendations' : '#/settings';
}

export default function App() {
  const [route, setRoute] = useState<StaticRoute | null>(() => getRoute(window.location.hash));
  const [settings, setSettings] = useState<ValidatedM9Settings | null>(() => loadSettings());
  const [selectedRecommendation, setSelectedRecommendation] = useState<SnapshotRecommendation | null>(null);
  const returnFocusRef = useRef<HTMLElement | null>(null);
  const staticSnapshot = useStaticMarketSnapshot();

  useEffect(() => {
    const onLocationChange = () => setRoute(getRoute(window.location.hash));
    window.addEventListener('popstate', onLocationChange);
    window.addEventListener('hashchange', onLocationChange);
    return () => {
      window.removeEventListener('popstate', onLocationChange);
      window.removeEventListener('hashchange', onLocationChange);
    };
  }, []);

  useEffect(() => {
    if (selectedRecommendation !== null || returnFocusRef.current === null) return;
    returnFocusRef.current.focus();
    returnFocusRef.current = null;
  }, [selectedRecommendation]);

  function navigate(target: StaticRoute) {
    if (target !== 'recommendations') returnFocusRef.current = null;
    setSelectedRecommendation(null);
    window.history.pushState({}, '', navigationTarget(target));
    setRoute(target);
  }

  function handleNavigation(event: MouseEvent<HTMLAnchorElement>, target: StaticRoute) {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    navigate(target);
  }

  function commitSettings(nextSettings: ValidatedM9Settings): boolean {
    try {
      saveSettings(nextSettings);
    } catch {
      return false;
    }

    setSettings(nextSettings);
    navigate('recommendations');
    return true;
  }

  function resetTutorial(): boolean {
    if (!clearSettings()) return false;
    setSettings(null);
    navigate('recommendations');
    return true;
  }

  function openRecommendation(recommendation: SnapshotRecommendation, trigger: HTMLElement) {
    returnFocusRef.current = trigger;
    setSelectedRecommendation(recommendation);
  }

  function closeRecommendation() {
    setSelectedRecommendation(null);
  }

  const recommendations = useMemo<SnapshotRecommendationResult | null>(() => {
    if (settings === null || staticSnapshot.state.kind !== 'ready' || staticSnapshot.freshness !== 'fresh') return null;
    return calculateSnapshotRecommendations(staticSnapshot.state.snapshot, {
      capitalCopper: settings.capitalCopper,
      riskProfile: settings.riskProfile,
    });
  }, [settings, staticSnapshot]);
  const canShowSelectedRecommendation = selectedRecommendation !== null && route === 'recommendations' && recommendations !== null;

  useEffect(() => {
    if (selectedRecommendation === null || canShowSelectedRecommendation) return;
    returnFocusRef.current = null;
    setSelectedRecommendation(null);
  }, [canShowSelectedRecommendation, selectedRecommendation]);

  if (route === null) {
    return (
      <div className="app-page">
        <main className="m9-shell route-unavailable" data-testid="unavailable-route">
          <p className="eyebrow">Tyrian Ledger</p>
          <h1>Route unavailable</h1>
          <p>This route is not part of the static market snapshot experience.</p>
          <a href="#/recommendations" onClick={(event) => handleNavigation(event, 'recommendations')}>Go to Recommendations</a>
        </main>
        <SiteFooter />
      </div>
    );
  }

  const isRecommendations = route === 'recommendations';
  return (
    <div className="app-page">
      <a className="skip-link" href="#main-content">Skip to main content</a>
      <div className="m9-shell">
        <header className="m9-header">
          <a aria-label="Tyrian Ledger home" className="brand-lockup" href="#/recommendations" onClick={(event) => handleNavigation(event, 'recommendations')}>
            <span aria-hidden="true" className="brand-mark">TL</span>
            <span><strong>Tyrian Ledger</strong><small>Unofficial market companion</small></span>
          </a>
          <nav aria-label="Primary navigation">
            <a aria-current={isRecommendations ? 'page' : undefined} href="#/recommendations" onClick={(event) => handleNavigation(event, 'recommendations')}>Recommendations</a>
            <a aria-current={isRecommendations ? undefined : 'page'} href="#/settings" onClick={(event) => handleNavigation(event, 'settings')}>Settings</a>
          </nav>
        </header>

        <main id="main-content">
          {route === 'recommendations'
            ? <RecommendationsPage
                recommendations={recommendations}
                settings={settings}
                snapshotState={staticSnapshot.state}
                freshness={staticSnapshot.freshness}
                nowMs={staticSnapshot.nowMs}
                onOpenRecommendation={openRecommendation}
                onOpenSettings={() => navigate('settings')}
              />
            : <SettingsPage settings={settings} onResetTutorial={resetTutorial} onSave={commitSettings} />}
        </main>
      </div>
      <SiteFooter />
      {canShowSelectedRecommendation && selectedRecommendation !== null && <OpportunityDetail onClose={closeRecommendation} recommendation={selectedRecommendation} />}
    </div>
  );
}

function useStaticMarketSnapshot(): {
  state: StaticSnapshotLoadState;
  freshness: SnapshotFreshness | null;
  nowMs: number;
} {
  const [state, setState] = useState<StaticSnapshotLoadState>({ kind: 'loading' });
  const [clockTick, setClockTick] = useState(() => Date.now());
  const requestRef = useRef<Promise<StaticSnapshotLoadState> | null>(null);
  const nowMs = Math.max(clockTick, Date.now());
  const freshness = state.kind === 'ready'
    ? classifySnapshotFreshness(state.snapshot.generatedAtUtc, nowMs)
    : null;

  useEffect(() => {
    let active = true;
    try {
      const url = resolveMarketSnapshotUrl(
        import.meta.env.BASE_URL,
        import.meta.env.VITE_MARKET_SNAPSHOT_PATH,
        window.location.origin,
      );
      requestRef.current ??= loadStaticMarketSnapshot(url);
      void requestRef.current.then((nextState) => {
        if (!active) return;
        setState(nextState);
        setClockTick(Date.now());
      });
    } catch {
      setState({ kind: 'unavailable', message: 'The configured market snapshot path is not available on this static site. Recommendations are unavailable.' });
    }

    return () => { active = false; };
  }, []);

  useEffect(() => {
    if (state.kind !== 'ready' || freshness !== 'fresh') return;
    const timeout = window.setTimeout(
      () => setClockTick(Date.now()),
      Math.min(60_000, millisecondsUntilSnapshotExpiry(state.snapshot.generatedAtUtc, nowMs)),
    );
    return () => window.clearTimeout(timeout);
  }, [freshness, nowMs, state]);

  return { state, freshness, nowMs };
}

function SettingsPage({
  settings,
  onSave,
  onResetTutorial,
}: {
  settings: ValidatedM9Settings | null;
  onSave: (settings: ValidatedM9Settings) => boolean;
  onResetTutorial: () => boolean;
}) {
  const [capital, setCapital] = useState<CapitalInput>(() => settings?.capital ?? { gold: '', silver: '', copper: '' });
  const [riskProfile, setRiskProfile] = useState<RiskProfile | null>(() => settings?.riskProfile ?? null);
  const [errors, setErrors] = useState<ReturnType<typeof validateSettings>['errors']>({});
  const [saveError, setSaveError] = useState<string | null>(null);
  const [resetError, setResetError] = useState<string | null>(null);

  function updateCapital(field: keyof CapitalInput, value: string) {
    setCapital((current) => ({ ...current, [field]: value }));
    setErrors((current) => ({ ...current, [field]: undefined }));
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSaveError(null);
    const validation = validateSettings(capital, riskProfile);
    setErrors(validation.errors);
    if (validation.settings === undefined) return;
    if (!onSave(validation.settings)) setSaveError('Your settings could not be saved in this browser. Please try again.');
  }

  function handleResetTutorial() {
    setSaveError(null);
    setResetError(null);
    if (!onResetTutorial()) setResetError('The tutorial could not be reset in this browser. Please try again.');
  }

  return (
    <section aria-labelledby="settings-title" className="m9-panel settings-panel">
      <p className="eyebrow">Your local preferences</p>
      <h1 id="settings-title">Set your trading guardrails</h1>
      <p className="page-introduction">Your capital and risk profile shape every suggestion. Tyrian Ledger stores only these preferences in this browser—it never uses an account or places a Trading Post order.</p>

      <form className="settings-form" noValidate onSubmit={handleSubmit}>
        <fieldset>
          <legend>Available capital</legend>
          <p className="field-help">Enter whole gold, silver, and copper. Leave a box empty to use zero.</p>
          <div className="capital-fields">
            <CapitalField id="capital-gold" label="Gold" value={capital.gold} error={errors.gold} onChange={(value) => updateCapital('gold', value)} />
            <CapitalField id="capital-silver" label="Silver" value={capital.silver} error={errors.silver} onChange={(value) => updateCapital('silver', value)} />
            <CapitalField id="capital-copper" label="Copper" value={capital.copper} error={errors.copper} onChange={(value) => updateCapital('copper', value)} />
          </div>
        </fieldset>

        <fieldset aria-describedby={errors.riskProfile ? 'risk-profile-error' : undefined}>
          <legend>Risk profile</legend>
          <p className="field-help">This controls the maximum capital committed to one opportunity and its minimum modeled return.</p>
          <div className="risk-profile-table-wrapper">
            <table className="risk-profile-table">
              <caption>Risk profile limits</caption>
              <thead><tr><th scope="col">Profile</th><th scope="col">Maximum spend</th><th scope="col">Minimum modeled ROI</th><th scope="col">Minimum modeled profit</th></tr></thead>
              <tbody>{(Object.keys(profileDetails) as RiskProfile[]).map((profile) => {
                const detail = profileDetails[profile];
                return <tr key={profile}><th scope="row">{detail.name}</th><td>{detail.spend}</td><td>{detail.roi}</td><td>{detail.profit}</td></tr>;
              })}</tbody>
            </table>
          </div>
          <div className="risk-options">
            {(Object.keys(profileDetails) as RiskProfile[]).map((profile) => {
              const detail = profileDetails[profile];
              return (
                <label className="risk-option" key={profile}>
                  <input checked={riskProfile === profile} name="risk-profile" onChange={() => {
                    setRiskProfile(profile);
                    setErrors((current) => ({ ...current, riskProfile: undefined }));
                  }} type="radio" value={profile} />
                  <span><strong>{detail.name}</strong><span>Maximum spend: {detail.spend}. Minimum modeled ROI: {detail.roi}. Minimum modeled profit: {detail.profit}.</span></span>
                </label>
              );
            })}
          </div>
          {errors.riskProfile && <p className="field-error" id="risk-profile-error" role="alert">{errors.riskProfile}</p>}
        </fieldset>

        <PrivacyNotice />
        {saveError && <p className="field-error" role="alert">{saveError}</p>}
        {resetError && <p className="field-error" role="alert">{resetError}</p>}
        <div className="settings-actions">
          <button className="primary-action" id="save-settings" type="submit">Save preferences</button>
          <button className="secondary-action" onClick={handleResetTutorial} type="button">Clear local preferences</button>
        </div>
        <p className="field-help reset-tutorial-help">Clearing removes only capital and risk saved on this device, then returns you to the setup prompt.</p>
      </form>
    </section>
  );
}

function CapitalField({
  id,
  label,
  value,
  error,
  onChange,
}: {
  id: string;
  label: string;
  value: string;
  error?: string;
  onChange: (value: string) => void;
}) {
  const errorId = `${id}-error`;
  return (
    <div className="capital-field">
      <label htmlFor={id}>{label}</label>
      <input aria-describedby={error ? errorId : undefined} aria-invalid={error ? true : undefined} id={id} inputMode="numeric" onChange={(event) => onChange(event.target.value)} pattern="[0-9]*" type="text" value={value} />
      {error && <p className="field-error" id={errorId} role="alert">{error}</p>}
    </div>
  );
}

function RecommendationsPage({
  settings,
  recommendations,
  snapshotState,
  freshness,
  nowMs,
  onOpenRecommendation,
  onOpenSettings,
}: {
  settings: ValidatedM9Settings | null;
  recommendations: SnapshotRecommendationResult | null;
  snapshotState: StaticSnapshotLoadState;
  freshness: SnapshotFreshness | null;
  nowMs: number;
  onOpenRecommendation: (recommendation: SnapshotRecommendation, trigger: HTMLElement) => void;
  onOpenSettings: () => void;
}) {
  return (
    <section aria-labelledby="recommendations-title" className="recommendations-page">
      <div className="page-heading">
        <div className="page-heading-copy">
          <p className="eyebrow">Published public market snapshot</p>
          <h1 id="recommendations-title">Choose your next move</h1>
          <p className="page-introduction">A short, ranked shortlist from the latest published market data. Every value is modeled guidance for a manual in-game decision—not a promise of a fill, sale, or profit.</p>
        </div>
        {settings !== null && <aside aria-label="Current settings" className="settings-summary"><span className="summary-label">Your available capital</span><strong>{formatCopper(settings.capitalCopper)}</strong><span>{profileDetails[settings.riskProfile].name} profile</span><button className="text-action" onClick={onOpenSettings} type="button">Adjust settings</button></aside>}
      </div>

      <SnapshotStateNotice state={snapshotState} freshness={freshness} nowMs={nowMs} />
      {settings === null
        ? <section aria-label="Set up recommendations" className="m9-panel guided-setup">
            <p className="eyebrow">Start here</p>
            <h2>Set a comfortable boundary</h2>
            <p>Tell us what you can spend and how cautious you want to be. These are the only preferences saved on this device.</p>
            <p>No account, API key, or game action is needed. You will create every buy order and sell listing yourself in Guild Wars 2.</p>
            <button className="primary-action" onClick={onOpenSettings} type="button">Set up capital and risk <span aria-hidden="true">→</span></button>
          </section>
        : recommendations !== null && <RecommendationResults onOpenRecommendation={onOpenRecommendation} result={recommendations} />}
    </section>
  );
}

function SnapshotStateNotice({
  state,
  freshness,
  nowMs,
}: {
  state: StaticSnapshotLoadState;
  freshness: SnapshotFreshness | null;
  nowMs: number;
}) {
  if (state.kind === 'loading') return <div aria-live="polite" className="snapshot-status is-loading" role="status"><span aria-hidden="true" className="status-orb" /><span><strong>Preparing the market ledger</strong><span>Loading the published market snapshot.</span></span></div>;
  if (state.kind !== 'ready') return <div className="snapshot-outcome" role="alert"><span aria-hidden="true" className="alert-mark">!</span><p><strong>Recommendations are paused.</strong> {state.message}</p></div>;

  const generatedAt = formatSnapshotTime(state.snapshot.generatedAtUtc);
  const age = formatSnapshotAge(state.snapshot.generatedAtUtc, nowMs);
  if (freshness === 'delayed') {
    return <div className="snapshot-outcome" role="alert"><span aria-hidden="true" className="alert-mark">!</span><p><strong>Snapshot refresh is delayed.</strong> This published data is {age} old, so recommendations are paused until a newer snapshot is available. Generated: {generatedAt}.</p></div>;
  }
  if (freshness === 'future') {
    return <div className="snapshot-outcome" role="alert"><span aria-hidden="true" className="alert-mark">!</span><p><strong>Snapshot timestamp cannot be trusted.</strong> {age} Recommendations are paused until this browser can assess a current snapshot. Generated: {generatedAt}.</p></div>;
  }

  return <div aria-live="polite" className="snapshot-status" role="status"><span aria-hidden="true" className="status-orb" /><span><strong>Compatible snapshot loaded.</strong><span>Generated: {generatedAt}. Data age: {age}.</span></span></div>;
}

function RecommendationResults({
  result,
  onOpenRecommendation,
}: {
  result: SnapshotRecommendationResult;
  onOpenRecommendation: (recommendation: SnapshotRecommendation, trigger: HTMLElement) => void;
}) {
  if (result.recommendations.length === 0) {
    return <section aria-labelledby="empty-results-title" className="empty-results"><p className="eyebrow">No safe match</p><h2 id="empty-results-title">No suggestions right now</h2><p>This compatible snapshot has no items that meet your capital and risk settings. Adjust your preferences, or return when a newer snapshot is published.</p></section>;
  }

  return <div className="recommendation-groups">
    {result.canActNow.length > 0 && <RecommendationGroup onOpenRecommendation={onOpenRecommendation} title="Can act now" description="Current listings support the suggested quantity at the planned buy price." recommendations={result.canActNow} />}
    {result.placeOrderAndWait.length > 0 && <RecommendationGroup onOpenRecommendation={onOpenRecommendation} title="Place an order and wait" description="The suggested buy order may need time to fill before you can list the item." recommendations={result.placeOrderAndWait} />}
  </div>;
}

function RecommendationGroup({
  title,
  description,
  recommendations,
  onOpenRecommendation,
}: {
  title: string;
  description: string;
  recommendations: SnapshotRecommendation[];
  onOpenRecommendation: (recommendation: SnapshotRecommendation, trigger: HTMLElement) => void;
}) {
  return <section aria-label={title} className="recommendation-group"><div className="group-heading"><div><p className="eyebrow">{recommendations.length} {recommendations.length === 1 ? 'opportunity' : 'opportunities'}</p><h2>{title}</h2></div><p>{description}</p></div><div className="recommendation-grid">{recommendations.map((recommendation) => <RecommendationCard key={`${recommendation.route}-${recommendation.itemId}-${recommendation.rank}`} onOpenRecommendation={onOpenRecommendation} recommendation={recommendation} />)}</div></section>;
}

function RecommendationCard({
  recommendation,
  onOpenRecommendation,
}: {
  recommendation: SnapshotRecommendation;
  onOpenRecommendation: (recommendation: SnapshotRecommendation, trigger: HTMLElement) => void;
}) {
  const isImmediate = recommendation.route === 'can-act-now';
  const itemCount = recommendation.quantity.toString();
  const routeLabel = isImmediate ? 'Act now' : 'Order and wait';
  const routeExplanation = isImmediate
    ? `Current sell listings at or below the planned buy price cover all ${itemCount} item${recommendation.quantity === 1n ? '' : 's'} in this suggestion.`
    : `Current sell listings do not cover all ${itemCount} item${recommendation.quantity === 1n ? '' : 's'} at the planned buy price, so the order may take time to fill.`;

  return (
    <article aria-labelledby={`recommendation-${recommendation.itemId}`} className="recommendation-card">
      <div className="card-topline"><span className={`route-badge ${isImmediate ? 'is-immediate' : ''}`}>{routeLabel}</span><span className="card-rank">Suggestion {recommendation.rank}</span></div>
      <h3 id={`recommendation-${recommendation.itemId}`}>{recommendation.itemName}</h3>
      <div className="profit-callout"><span>Modeled profit</span><strong>{formatCopper(recommendation.modeledProfitCopper)}</strong><span>{formatModeledRoi(recommendation.modeledRoi)} modeled ROI</span></div>
      <dl className="recommendation-values">
        <div><dt>Quantity</dt><dd>{itemCount}</dd></div>
        <div><dt>Capital needed</dt><dd>{formatCopper(recommendation.totalCostCopper)}</dd></div>
        <div><dt>Buy at</dt><dd>{formatCopper(recommendation.buyUnitPriceCopper)} each</dd></div>
        <div><dt>List at</dt><dd>{formatCopper(recommendation.saleUnitPriceCopper)} each</dd></div>
      </dl>
      <p className="route-explanation">{routeExplanation}</p>
      <p className="card-safety"><strong>Current-book guard passed.</strong> This uses published order-book depth and spread checks; it does not guarantee a fill or sale.</p>
      <button className="card-action" onClick={(event) => onOpenRecommendation(recommendation, event.currentTarget)} type="button">View manual trade plan <span aria-hidden="true">→</span></button>
    </article>
  );
}

function OpportunityDetail({ recommendation, onClose }: { recommendation: SnapshotRecommendation; onClose: () => void }) {
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useRef<HTMLElement>(null);
  const isImmediate = recommendation.route === 'can-act-now';
  const itemCount = recommendation.quantity.toString();
  const routeLabel = isImmediate ? 'Can act now' : 'Place an order and wait';
  const manualSteps = isImmediate
    ? ['Open the Trading Post in Guild Wars 2 and search for this item.', `Create the shown buy order for ${itemCount} at ${formatCopper(recommendation.buyUnitPriceCopper)} each.`, `When it fills, create the shown sell listing at ${formatCopper(recommendation.saleUnitPriceCopper)} each.`]
    : ['Open the Trading Post in Guild Wars 2 and search for this item.', `Create the shown buy order for ${itemCount} at ${formatCopper(recommendation.buyUnitPriceCopper)} each.`, `Wait for that buy order to fill, then create the shown sell listing at ${formatCopper(recommendation.saleUnitPriceCopper)} each.`];

  useEffect(() => {
    closeButtonRef.current?.focus();
  }, []);

  function handleKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'Escape') {
      event.preventDefault();
      onClose();
      return;
    }
    if (event.key !== 'Tab') return;
    const focusable = dialogRef.current?.querySelectorAll<HTMLElement>('a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])');
    if (focusable === undefined || focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  return (
    <div className="detail-backdrop">
      <section aria-describedby="opportunity-detail-description" aria-labelledby="opportunity-detail-title" aria-modal="true" className="opportunity-detail" onKeyDown={handleKeyDown} ref={dialogRef} role="dialog">
        <header className="detail-header">
          <div><p className="eyebrow">Manual trade plan</p><h2 id="opportunity-detail-title">{recommendation.itemName}</h2></div>
          <button aria-label="Close manual trade plan" className="close-detail" onClick={onClose} ref={closeButtonRef} type="button">×</button>
        </header>
        <p className="detail-introduction" id="opportunity-detail-description"><span className={`route-badge ${isImmediate ? 'is-immediate' : ''}`}>{routeLabel}</span> Review the exact plan before you choose whether to act in-game.</p>

        <section aria-label="At a glance" className="detail-hero-metrics">
          <div><span>Modeled profit</span><strong>{formatCopper(recommendation.modeledProfitCopper)}</strong></div>
          <div><span>Modeled ROI</span><strong>{formatModeledRoi(recommendation.modeledRoi)}</strong></div>
          <div><span>Total capital</span><strong>{formatCopper(recommendation.totalCostCopper)}</strong></div>
        </section>

        <div className="detail-grid">
          <section aria-labelledby="pricing-title" className="detail-section"><h3 id="pricing-title">Pricing and fees</h3><dl className="detail-values">
            <div><dt>Quantity</dt><dd>{itemCount}</dd></div>
            <div><dt>Buy order</dt><dd>{formatCopper(recommendation.buyUnitPriceCopper)} each</dd></div>
            <div><dt>Sale listing</dt><dd>{formatCopper(recommendation.saleUnitPriceCopper)} each</dd></div>
            <div><dt>Buy order reserve</dt><dd>{formatCopper(recommendation.buyOrderReserveCopper)}</dd></div>
            <div><dt>Listing fee</dt><dd>{formatCopper(recommendation.listingFeeCopper)}</dd></div>
            <div><dt>Exchange fee</dt><dd>{formatCopper(recommendation.exchangeFeeCopper)}</dd></div>
            <div><dt>Net sale proceeds</dt><dd>{formatCopper(recommendation.netSaleProceedsCopper)}</dd></div>
            <div><dt>Total cost</dt><dd>{formatCopper(recommendation.totalCostCopper)}</dd></div>
          </dl></section>
          <section aria-labelledby="evidence-title" className="detail-section evidence-section"><h3 id="evidence-title">Why this is grouped here</h3>
            <p>{isImmediate ? `Published sell listings at or below the planned buy price cover all ${itemCount} items.` : `Published sell listings do not yet cover all ${itemCount} items at the planned buy price, so the buy order may take time to fill.`}</p>
            <p><strong>Liquidity guard:</strong> The candidate passed the fixed current-order-book depth and spread checks before recommendation ranking. This is evidence from one snapshot, not a prediction of execution.</p>
            <p className="snapshot-time"><strong>Snapshot generated:</strong> {formatSnapshotTime(recommendation.snapshotGeneratedAtUtc)}</p>
          </section>
        </div>

        <section aria-labelledby="manual-steps-title" className="manual-steps"><div><p className="eyebrow">In-game only</p><h3 id="manual-steps-title">Follow this checklist manually</h3></div><ol>{manualSteps.map((step) => <li key={step}>{step}</li>)}</ol></section>

        <section aria-labelledby="assumptions-title" className="assumptions-panel"><h3 id="assumptions-title">Model assumptions and limits</h3><p>This plan is calculated from one published market snapshot. It cannot guarantee a price, an order fill, a sale, or profit.</p><ul>{recommendation.assumptions.map((assumption) => <li key={assumption}>{assumptionMessage(assumption)}</li>)}</ul></section>
        <div className="detail-actions"><button className="secondary-action" onClick={onClose} type="button">Back to recommendations</button></div>
      </section>
    </div>
  );
}

function PrivacyNotice() {
  return <aside className="privacy-notice"><p className="eyebrow">Private by design</p><h2>Only your preferences stay here</h2><p>Your capital and risk profile are saved locally in this browser. Tyrian Ledger does not ask for an account, API key, or personal Trading Post history, and it cannot act in-game for you.</p></aside>;
}

function SiteFooter() {
  return <footer className="site-footer"><div><strong>Tyrian Ledger</strong><span>Independent, read-only market guidance.</span></div><p>Tyrian Ledger is an unofficial, independent Guild Wars 2 fan site and is not affiliated with or endorsed by ArenaNet or NCSOFT.</p><p>Guild Wars 2 © ArenaNet, LLC. All rights reserved. Guild Wars 2 and GW2 are trademarks or registered trademarks of NCSOFT Corporation.</p></footer>;
}

function assumptionMessage(assumption: string): string {
  const messages: Record<string, string> = {
    'current-order-book-snapshot-only': 'Uses the published order book snapshot only.',
    'current-order-book-depth-and-spread-guard': 'Passed fixed order-book depth and relative-spread checks. This does not guarantee a fill or sale.',
    'manual-in-game-orders-required': 'Every Trading Post action remains manual.',
    'no-execution-sale-or-profit-guarantee': 'No execution, sale, or profit is guaranteed.',
    'fee-rounding-pending-external-verification': 'Fee rounding is a current model assumption.',
  };
  return messages[assumption] ?? 'Uses the published public market data available to this snapshot.';
}
