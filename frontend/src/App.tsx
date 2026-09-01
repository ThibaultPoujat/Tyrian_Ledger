import { type FormEvent, type MouseEvent, useEffect, useMemo, useRef, useState } from 'react';
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

function getRoute(pathname: string): StaticRoute | null {
  switch (pathname) {
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
  return route === 'recommendations' ? '/recommendations' : '/settings';
}

export default function App() {
  const [route, setRoute] = useState<StaticRoute | null>(() => getRoute(window.location.pathname));
  const [settings, setSettings] = useState<ValidatedM9Settings | null>(() => loadSettings());
  const staticSnapshot = useStaticMarketSnapshot();

  useEffect(() => {
    const onPopState = () => setRoute(getRoute(window.location.pathname));
    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, []);

  function navigate(target: StaticRoute) {
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

  const recommendations = useMemo<SnapshotRecommendationResult | null>(() => {
    if (settings === null || staticSnapshot.state.kind !== 'ready' || staticSnapshot.freshness !== 'fresh') return null;
    return calculateSnapshotRecommendations(staticSnapshot.state.snapshot, {
      capitalCopper: settings.capitalCopper,
      riskProfile: settings.riskProfile,
    });
  }, [settings, staticSnapshot]);

  if (route === null) {
    return (
      <main className="m9-shell" data-testid="unavailable-route">
        <p className="eyebrow">Tyrian Ledger</p>
        <h1>Route unavailable</h1>
        <p>This route is not part of the static market snapshot experience.</p>
        <a href="/recommendations" onClick={(event) => handleNavigation(event, 'recommendations')}>Go to Recommendations</a>
      </main>
    );
  }

  const isRecommendations = route === 'recommendations';
  return (
    <main className="m9-shell">
      <header className="m9-header">
        <p className="eyebrow">Tyrian Ledger</p>
        <nav aria-label="Primary navigation">
          <a aria-current={isRecommendations ? 'page' : undefined} href="/recommendations" onClick={(event) => handleNavigation(event, 'recommendations')}>Recommendations</a>
          <a aria-current={isRecommendations ? undefined : 'page'} href="/settings" onClick={(event) => handleNavigation(event, 'settings')}>Settings</a>
        </nav>
      </header>

      {route === 'recommendations'
        ? <RecommendationsPage
            recommendations={recommendations}
            settings={settings}
            snapshotState={staticSnapshot.state}
            freshness={staticSnapshot.freshness}
            nowMs={staticSnapshot.nowMs}
            onOpenSettings={() => navigate('settings')}
          />
        : <SettingsPage settings={settings} onResetTutorial={resetTutorial} onSave={commitSettings} />}
    </main>
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
    <section aria-labelledby="settings-title" className="m9-panel">
      <p className="eyebrow">Your starting point</p>
      <h1 id="settings-title">Settings</h1>
      <p className="page-introduction">Tell Tyrian Ledger what you can spend and how much risk feels comfortable. This never places an order for you.</p>

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
          <p className="field-help">This sets the maximum spend and the minimum modeled return for each suggestion.</p>
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

        {saveError && <p className="field-error" role="alert">{saveError}</p>}
        {resetError && <p className="field-error" role="alert">{resetError}</p>}
        <div className="settings-actions">
          <button className="primary-action" id="save-settings" type="submit">Save settings</button>
          <button className="secondary-action" onClick={handleResetTutorial} type="button">Reset tutorial</button>
        </div>
        <p className="field-help reset-tutorial-help">This clears only your saved capital and risk on this device, then returns you to the tutorial.</p>
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
  onOpenSettings,
}: {
  settings: ValidatedM9Settings | null;
  recommendations: SnapshotRecommendationResult | null;
  snapshotState: StaticSnapshotLoadState;
  freshness: SnapshotFreshness | null;
  nowMs: number;
  onOpenSettings: () => void;
}) {
  return (
    <section aria-labelledby="recommendations-title" className="recommendations-page">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Published public market snapshot</p>
          <h1 id="recommendations-title">Recommendations</h1>
          <p className="page-introduction">Suggestions are recalculated in this browser from the published market snapshot. They are modeled guidance, not a promise that an order will fill, sell, or make a profit.</p>
        </div>
        {settings !== null && <aside aria-label="Current settings" className="settings-summary"><strong>{formatCopper(settings.capitalCopper)}</strong><span>{profileDetails[settings.riskProfile].name} risk</span></aside>}
      </div>

      <SnapshotStateNotice state={snapshotState} freshness={freshness} nowMs={nowMs} />
      {settings === null
        ? <section aria-label="Set up recommendations" className="m9-panel guided-setup">
            <p className="eyebrow">A short guided setup</p>
            <h2>Choose your preferences</h2>
            <p>Start with the amount of gold you are comfortable spending and choose how cautious you want each suggestion to be.</p>
            <p>You will always create every buy order and sell listing yourself in the Guild Wars 2 Trading Post. Tyrian Ledger only explains a possible next step.</p>
            <button className="primary-action" onClick={onOpenSettings} type="button">Set up my capital and risk</button>
          </section>
        : recommendations !== null && <RecommendationResults result={recommendations} />}
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
  if (state.kind === 'loading') return <p className="snapshot-status" role="status">Loading the published market snapshot.</p>;
  if (state.kind !== 'ready') return <div className="snapshot-outcome" role="alert"><p>{state.message}</p></div>;

  const generatedAt = formatSnapshotTime(state.snapshot.generatedAtUtc);
  const age = formatSnapshotAge(state.snapshot.generatedAtUtc, nowMs);
  if (freshness === 'delayed') {
    return <div className="snapshot-outcome" role="alert"><p><strong>Snapshot refresh is delayed.</strong> This published data is {age} old, so recommendations are paused until a newer snapshot is available. Generated: {generatedAt}.</p></div>;
  }
  if (freshness === 'future') {
    return <div className="snapshot-outcome" role="alert"><p><strong>Snapshot timestamp cannot be trusted.</strong> {age} Recommendations are paused until this browser can assess a current snapshot. Generated: {generatedAt}.</p></div>;
  }

  return <div className="snapshot-status" role="status"><strong>Compatible snapshot loaded.</strong><span>Generated: {generatedAt}. Data age: {age}.</span></div>;
}

function RecommendationResults({ result }: { result: SnapshotRecommendationResult }) {
  if (result.recommendations.length === 0) {
    return <section aria-labelledby="empty-results-title" className="empty-results"><h2 id="empty-results-title">No suggestions right now</h2><p>This compatible snapshot has no items that meet your capital and risk settings. Check back after a newer snapshot is published or adjust your preferences.</p></section>;
  }

  return <div className="recommendation-groups">
    {result.canActNow.length > 0 && <RecommendationGroup title="Can act now" recommendations={result.canActNow} />}
    {result.placeOrderAndWait.length > 0 && <RecommendationGroup title="Place an order and wait" recommendations={result.placeOrderAndWait} />}
  </div>;
}

function RecommendationGroup({ title, recommendations }: { title: string; recommendations: SnapshotRecommendation[] }) {
  return <section aria-label={title} className="recommendation-group"><h2>{title}</h2><div className="recommendation-grid">{recommendations.map((recommendation) => <RecommendationCard key={`${recommendation.route}-${recommendation.itemId}-${recommendation.rank}`} recommendation={recommendation} />)}</div></section>;
}

function RecommendationCard({ recommendation }: { recommendation: SnapshotRecommendation }) {
  const isImmediate = recommendation.route === 'can-act-now';
  const itemCount = recommendation.quantity.toString();
  const routeExplanation = isImmediate
    ? `Current sell listings at or below the shown buy price cover all ${itemCount} item${recommendation.quantity === 1n ? '' : 's'} in this suggestion.`
    : `Current sell listings do not cover all ${itemCount} item${recommendation.quantity === 1n ? '' : 's'} at the shown buy price, so the buy order may take time to fill.`;
  const manualSteps = isImmediate
    ? ['Open the Trading Post in Guild Wars 2 and search for this item.', `Create the shown buy order for ${itemCount} at ${formatCopper(recommendation.buyUnitPriceCopper)} each.`, `When it fills, create the shown sell listing at ${formatCopper(recommendation.saleUnitPriceCopper)} each.`]
    : ['Open the Trading Post in Guild Wars 2 and search for this item.', `Create the shown buy order for ${itemCount} at ${formatCopper(recommendation.buyUnitPriceCopper)} each.`, `Wait for that buy order to fill, then create the shown sell listing at ${formatCopper(recommendation.saleUnitPriceCopper)} each.`];

  return (
    <article className="recommendation-card">
      <p className="card-rank">Suggestion {recommendation.rank}</p>
      <h3>{recommendation.itemName}</h3>
      <p className="route-explanation">{routeExplanation}</p>
      <dl className="recommendation-values">
        <div><dt>Quantity</dt><dd>{itemCount}</dd></div>
        <div><dt>Buy price</dt><dd>{formatCopper(recommendation.buyUnitPriceCopper)} each</dd></div>
        <div><dt>Sale price</dt><dd>{formatCopper(recommendation.saleUnitPriceCopper)} each</dd></div>
        <div><dt>Total cost (buy order + listing fee)</dt><dd>{formatCopper(recommendation.totalCostCopper)}</dd></div>
        <div><dt>Listing fee</dt><dd>{formatCopper(recommendation.listingFeeCopper)}</dd></div>
        <div><dt>Exchange fee</dt><dd>{formatCopper(recommendation.exchangeFeeCopper)}</dd></div>
        <div><dt>Modeled profit</dt><dd>{formatCopper(recommendation.modeledProfitCopper)}</dd></div>
        <div><dt>Modeled ROI</dt><dd>{formatModeledRoi(recommendation.modeledRoi)}</dd></div>
      </dl>
      <p className="snapshot-time">Snapshot generated: {formatSnapshotTime(recommendation.snapshotGeneratedAtUtc)}</p>
      <div className="manual-steps"><h4>Manual in-game steps</h4><ol>{manualSteps.map((step) => <li key={step}>{step}</li>)}</ol></div>
      <p className="card-disclaimer">This is modeled guidance from one published market snapshot. It does not guarantee a fill, sale, or profit.</p>
      <ul className="assumption-list" aria-label="Recommendation assumptions">{recommendation.assumptions.map((assumption) => <li key={assumption}>{assumptionMessage(assumption)}</li>)}</ul>
    </article>
  );
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
