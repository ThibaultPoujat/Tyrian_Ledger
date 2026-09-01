import { type FormEvent, type MouseEvent, useEffect, useState } from 'react';
import './App.css';
import {
  cancelScan,
  clearSettings,
  formatCopper,
  formatModeledRoi,
  formatScanTime,
  getScanStatus,
  idleScanSnapshot,
  loadSettings,
  type CapitalInput,
  type Recommendation,
  type RiskProfile,
  type ScanSnapshot,
  saveSettings,
  SCAN_STATUS_POLL_INTERVAL_MS,
  startScan,
  type ValidatedM9Settings,
  validateSettings,
} from './m9';

type M9Route = 'recommendations' | 'settings';

const profileDetails: Record<RiskProfile, { name: string; spend: string; roi: string; profit: string }> = {
  cautious: { name: 'Cautious', spend: '10% of your capital', roi: '5% modeled ROI', profit: '10 silver modeled profit' },
  balanced: { name: 'Balanced', spend: '25% of your capital', roi: '8% modeled ROI', profit: '25 silver modeled profit' },
  adventurous: { name: 'Adventurous', spend: '50% of your capital', roi: '12% modeled ROI', profit: '50 silver modeled profit' },
};

function getRoute(pathname: string): M9Route | null {
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

function safeSnapshot(snapshot: ScanSnapshot): ScanSnapshot {
  if (snapshot.state === 'complete' && snapshot.result !== null) return snapshot;
  if (snapshot.state === 'complete') {
    return { state: 'failed', progress: null, isRetryable: true, result: null };
  }

  return {
    ...snapshot,
    progress: snapshot.state === 'running' ? snapshot.progress : null,
    result: null,
  };
}

function safeCancelledSnapshot(snapshot: ScanSnapshot): ScanSnapshot {
  return snapshot.state === 'cancelled'
    ? safeSnapshot(snapshot)
    : { state: 'failed', progress: null, isRetryable: true, result: null };
}

function navigationTarget(route: M9Route): string {
  return route === 'recommendations' ? '/recommendations' : '/settings';
}

export default function App() {
  const [route, setRoute] = useState<M9Route | null>(() => getRoute(window.location.pathname));
  const [settings, setSettings] = useState<ValidatedM9Settings | null>(() => loadSettings());

  useEffect(() => {
    const onPopState = () => setRoute(getRoute(window.location.pathname));
    window.addEventListener('popstate', onPopState);
    return () => window.removeEventListener('popstate', onPopState);
  }, []);

  function navigate(target: M9Route) {
    window.history.pushState({}, '', navigationTarget(target));
    setRoute(target);
  }

  function handleNavigation(event: MouseEvent<HTMLAnchorElement>, target: M9Route) {
    if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    navigate(target);
  }

  if (route === null) {
    return (
      <main className="m9-shell" data-testid="unavailable-route">
        <p className="eyebrow">Tyrian Ledger</p>
        <h1>Route unavailable</h1>
        <p>This route is not part of the beginner fast-flip MVP.</p>
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
        ? <RecommendationsPage settings={settings} onOpenSettings={() => navigate('settings')} />
        : <SettingsPage settings={settings} onSave={(nextSettings) => {
          setSettings(nextSettings);
          navigate('recommendations');
        }} onResetTutorial={() => {
          if (!clearSettings()) return false;
          setSettings(null);
          navigate('recommendations');
          return true;
        }} />}
    </main>
  );
}

function SettingsPage({
  settings,
  onSave,
  onResetTutorial,
}: {
  settings: ValidatedM9Settings | null;
  onSave: (settings: ValidatedM9Settings) => void;
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

    try {
      saveSettings(validation.settings);
      onSave(validation.settings);
    } catch {
      setSaveError('Your settings could not be saved in this browser. Please try again.');
    }
  }

  function handleResetTutorial() {
    setSaveError(null);
    setResetError(null);
    if (!onResetTutorial()) {
      setResetError('The tutorial could not be reset in this browser. Please try again.');
    }
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
          <div className="risk-options">
            {(Object.keys(profileDetails) as RiskProfile[]).map((profile) => {
              const detail = profileDetails[profile];
              return (
                <label className="risk-option" key={profile}>
                  <input checked={riskProfile === profile} name="risk-profile" onChange={() => {
                    setRiskProfile(profile);
                    setErrors((current) => ({ ...current, riskProfile: undefined }));
                  }} type="radio" value={profile} />
                  <span><strong>{detail.name}</strong><span>Maximum spend: {detail.spend}. Minimum: {detail.roi} and {detail.profit}.</span></span>
                </label>
              );
            })}
          </div>
          {errors.riskProfile && <p className="field-error" id="risk-profile-error" role="alert">{errors.riskProfile}</p>}
        </fieldset>

        {saveError && <p className="field-error" role="alert">{saveError}</p>}
        {resetError && <p className="field-error" role="alert">{resetError}</p>}
        <div className="settings-actions">
          <button className="primary-action" type="submit">Save settings</button>
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
  onOpenSettings,
}: {
  settings: ValidatedM9Settings | null;
  onOpenSettings: () => void;
}) {
  const [snapshot, setSnapshot] = useState<ScanSnapshot>(idleScanSnapshot);
  const [isStarting, setIsStarting] = useState(false);
  const [isCancelling, setIsCancelling] = useState(false);
  const [requestError, setRequestError] = useState<string | null>(null);

  useEffect(() => {
    if (snapshot.state !== 'running' || isStarting || isCancelling) return;
    let active = true;
    let timer: number | undefined;
    const poll = async () => {
      try {
        const next = safeSnapshot(await getScanStatus());
        if (!active) return;
        setSnapshot(next);
        if (next.state === 'running') timer = window.setTimeout(() => void poll(), SCAN_STATUS_POLL_INTERVAL_MS);
      } catch {
        if (!active) return;
        setRequestError('The scan status could not be checked. No recommendations were kept.');
        setSnapshot({ state: 'failed', progress: null, isRetryable: true, result: null });
      }
    };

    timer = window.setTimeout(() => void poll(), SCAN_STATUS_POLL_INTERVAL_MS);
    return () => {
      active = false;
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [isCancelling, isStarting, snapshot.state]);

  async function handleStart() {
    if (settings === null) return;
    setRequestError(null);
    setSnapshot(idleScanSnapshot);
    setIsStarting(true);
    try {
      setSnapshot(safeSnapshot(await startScan(settings)));
    } catch {
      setRequestError('The scan could not be started. No recommendations were kept.');
      setSnapshot({ state: 'failed', progress: null, isRetryable: true, result: null });
    } finally {
      setIsStarting(false);
    }
  }

  async function handleCancel() {
    setRequestError(null);
    setIsCancelling(true);
    setSnapshot((current) => ({ ...current, result: null }));
    try {
      setSnapshot(safeCancelledSnapshot(await cancelScan()));
    } catch {
      setRequestError('The scan could not be cancelled safely. No recommendations were kept.');
      setSnapshot({ state: 'failed', progress: null, isRetryable: true, result: null });
    } finally {
      setIsCancelling(false);
    }
  }

  if (settings === null) {
    return (
      <section aria-labelledby="recommendations-title" className="m9-panel guided-setup">
        <p className="eyebrow">A short guided setup</p>
        <h1 id="recommendations-title">Recommendations</h1>
        <p>Start with the amount of gold you are comfortable spending and choose how cautious you want each suggestion to be.</p>
        <p>You will always create every buy order and sell listing yourself in the Guild Wars 2 Trading Post. Tyrian Ledger only explains a possible next step.</p>
        <button className="primary-action" onClick={onOpenSettings} type="button">Set up my capital and risk</button>
      </section>
    );
  }

  const profile = profileDetails[settings.riskProfile];
  return (
    <section aria-labelledby="recommendations-title" className="recommendations-page">
      <div className="page-heading">
        <div>
          <p className="eyebrow">Current public market only</p>
          <h1 id="recommendations-title">Recommendations</h1>
          <p className="page-introduction">Scan the market when you are ready. Results are modeled guidance, not a promise that an order will fill, sell, or make a profit.</p>
        </div>
        <aside aria-label="Current settings" className="settings-summary"><strong>{formatCopper(settings.capitalCopper)}</strong><span>{profile.name} risk</span></aside>
      </div>

      <div className="scan-actions">
        <button className="primary-action" disabled={isStarting || isCancelling || snapshot.state === 'running'} onClick={() => void handleStart()} type="button">{isStarting ? 'Starting scan…' : 'Scan the market'}</button>
        {snapshot.state === 'running' && !isStarting && <button className="secondary-action" disabled={isCancelling} onClick={() => void handleCancel()} type="button">{isCancelling ? 'Cancelling scan…' : 'Cancel scan'}</button>}
      </div>

      <ScanStateNotice isCancelling={isCancelling} isStarting={isStarting} onRetry={() => void handleStart()} requestError={requestError} snapshot={snapshot} />
      {snapshot.state === 'complete' && snapshot.result !== null && <RecommendationResults result={snapshot.result} />}
    </section>
  );
}

function ScanStateNotice({
  snapshot,
  isStarting,
  isCancelling,
  requestError,
  onRetry,
}: {
  snapshot: ScanSnapshot;
  isStarting: boolean;
  isCancelling: boolean;
  requestError: string | null;
  onRetry: () => void;
}) {
  if (isStarting) return <p className="scan-status" role="status">Starting your player-requested market scan.</p>;
  if (isCancelling) return <p className="scan-status" role="status">Cancelling the active scan. Recommendations will not be shown.</p>;
  if (snapshot.state === 'running') return <p className="scan-status" role="status">{progressMessage(snapshot.progress?.stage, snapshot.progress?.finalistCount ?? null)}</p>;
  if (snapshot.state === 'idle') return <p className="scan-status" role="status">No scan has run yet.</p>;
  if (snapshot.state === 'complete') return <p className="scan-status" role="status">Scan complete. These suggestions use a single current market snapshot.</p>;

  const message = requestError ?? terminalMessage(snapshot.state);
  return <div className="scan-outcome" role="alert"><p>{message}</p>{snapshot.isRetryable && <button className="secondary-action" onClick={onRetry} type="button">Retry scan</button>}</div>;
}

function progressMessage(stage: string | undefined, finalistCount: number | null): string {
  const stageMessage: Record<string, string> = {
    'discovering-price-item-ids': 'Finding public Trading Post items.',
    'discovering-aggregate-prices': 'Reading current public market prices.',
    'screening-candidates': 'Screening possible fast flips.',
    'reading-finalist-listings': 'Checking detailed listings for the strongest candidates.',
    'reading-finalist-metadata': 'Adding item names for the strongest candidates.',
    'calculating-recommendations': 'Calculating modeled costs, fees, and returns.',
  };
  const base = stageMessage[stage ?? ''] ?? 'Checking the current public market.';
  return finalistCount === null ? base : `${base} ${finalistCount} finalists need detailed checks.`;
}

function terminalMessage(state: ScanSnapshot['state']): string {
  switch (state) {
    case 'cancelled': return 'Scan cancelled. No recommendations were kept.';
    case 'rate-limited': return 'The public market asked us to slow down. No recommendations were kept; try again shortly.';
    default: return 'The scan did not finish. No recommendations were kept; please try again.';
  }
}

function RecommendationResults({ result }: { result: NonNullable<ScanSnapshot['result']> }) {
  const recommendations = [...result.canActNow, ...result.placeOrderAndWait].sort((first, second) => first.rank - second.rank).slice(0, 5);
  const canActNow = recommendations.filter((recommendation) => recommendation.route === 'can-act-now');
  const placeOrderAndWait = recommendations.filter((recommendation) => recommendation.route === 'place-order-and-wait');
  if (recommendations.length === 0) {
    return <section aria-labelledby="empty-results-title" className="empty-results"><h2 id="empty-results-title">No suggestions right now</h2><p>This scan completed, but no current items met your capital and risk settings. You can scan again later.</p></section>;
  }

  return <div className="recommendation-groups">{canActNow.length > 0 && <RecommendationGroup title="Can act now" recommendations={canActNow} />}{placeOrderAndWait.length > 0 && <RecommendationGroup title="Place an order and wait" recommendations={placeOrderAndWait} />}</div>;
}

function RecommendationGroup({ title, recommendations }: { title: string; recommendations: Recommendation[] }) {
  return <section aria-label={title} className="recommendation-group"><h2>{title}</h2><div className="recommendation-grid">{recommendations.map((recommendation) => <RecommendationCard key={`${recommendation.route}-${recommendation.itemId}-${recommendation.rank}`} recommendation={recommendation} />)}</div></section>;
}

function RecommendationCard({ recommendation }: { recommendation: Recommendation }) {
  const isImmediate = recommendation.route === 'can-act-now';
  const routeExplanation = isImmediate
    ? `Current sell listings at or below the shown buy price cover all ${recommendation.quantity} item${recommendation.quantity === 1 ? '' : 's'} in this suggestion.`
    : `Current sell listings do not cover all ${recommendation.quantity} item${recommendation.quantity === 1 ? '' : 's'} at the shown buy price, so the buy order may take time to fill.`;
  const manualSteps = isImmediate
    ? ['Open the Trading Post in Guild Wars 2 and search for this item.', `Create the shown buy order for ${recommendation.quantity} at ${formatCopper(recommendation.buyUnitPriceCopper)} each.`, `When it fills, create the shown sell listing at ${formatCopper(recommendation.saleUnitPriceCopper)} each.`]
    : ['Open the Trading Post in Guild Wars 2 and search for this item.', `Create the shown buy order for ${recommendation.quantity} at ${formatCopper(recommendation.buyUnitPriceCopper)} each.`, `Wait for that buy order to fill, then create the shown sell listing at ${formatCopper(recommendation.saleUnitPriceCopper)} each.`];

  return (
    <article className="recommendation-card">
      <p className="card-rank">Suggestion {recommendation.rank}</p>
      <h3>{recommendation.itemName}</h3>
      <p className="route-explanation">{routeExplanation}</p>
      <dl className="recommendation-values">
        <div><dt>Quantity</dt><dd>{recommendation.quantity}</dd></div>
        <div><dt>Buy price</dt><dd>{formatCopper(recommendation.buyUnitPriceCopper)} each</dd></div>
        <div><dt>Sale price</dt><dd>{formatCopper(recommendation.saleUnitPriceCopper)} each</dd></div>
        <div><dt>Total cost (buy order + listing fee)</dt><dd>{formatCopper(recommendation.totalCostCopper)}</dd></div>
        <div><dt>Listing fee</dt><dd>{formatCopper(recommendation.listingFeeCopper)}</dd></div>
        <div><dt>Exchange fee</dt><dd>{formatCopper(recommendation.exchangeFeeCopper)}</dd></div>
        <div><dt>Modeled profit</dt><dd>{formatCopper(recommendation.modeledProfitCopper)}</dd></div>
        <div><dt>Modeled ROI</dt><dd>{formatModeledRoi(recommendation.modeledRoi)}</dd></div>
      </dl>
      <p className="scan-time">Scan time: {formatScanTime(recommendation.scanCompletedAtUtc)}</p>
      <div className="manual-steps"><h4>Manual in-game steps</h4><ol>{manualSteps.map((step) => <li key={step}>{step}</li>)}</ol></div>
      <p className="card-disclaimer">This is modeled guidance from one current snapshot. It does not guarantee a fill, sale, or profit.</p>
      <ul className="assumption-list" aria-label="Recommendation assumptions">{recommendation.assumptions.map((assumption) => <li key={assumption}>{assumptionMessage(assumption)}</li>)}</ul>
    </article>
  );
}

function assumptionMessage(assumption: string): string {
  const messages: Record<string, string> = {
    'current-order-book-snapshot-only': 'Uses the current order book snapshot only.',
    'manual-in-game-orders-required': 'Every Trading Post action remains manual.',
    'no-execution-sale-or-profit-guarantee': 'No execution, sale, or profit is guaranteed.',
    'fee-rounding-pending-external-verification': 'Fee rounding is a current model assumption.',
  };
  return messages[assumption] ?? 'Uses the current public market data available to this scan.';
}
