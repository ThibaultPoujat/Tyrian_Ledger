import { type FormEvent, type KeyboardEvent, type MouseEvent, type SetStateAction, useCallback, useEffect, useRef, useState } from 'react';
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

interface ScanSession {
  snapshot: ScanSnapshot;
  isStarting: boolean;
  isCancelling: boolean;
  requestError: string | null;
}

interface PendingSettingsChange {
  settings: ValidatedM9Settings;
}

type TutorialResetResult = 'reset' | 'cancellation-failed' | 'storage-failed';

interface PlayerScanController {
  session: ScanSession;
  isActive: boolean;
  start: (settings: ValidatedM9Settings) => Promise<void>;
  cancel: () => Promise<void>;
  discard: () => void;
  cancelForSettingsChange: () => Promise<boolean>;
}

const idleScanSession: ScanSession = {
  snapshot: idleScanSnapshot,
  isStarting: false,
  isCancelling: false,
  requestError: null,
};

const profileDetails: Record<RiskProfile, { name: string; spend: string; roi: string; profit: string }> = {
  cautious: { name: 'Cautious', spend: '10% of capital', roi: '5%', profit: '10 silver' },
  balanced: { name: 'Balanced', spend: '25% of capital', roi: '8%', profit: '25 silver' },
  adventurous: { name: 'Adventurous', spend: '50% of capital', roi: '12%', profit: '50 silver' },
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

function settingsMatch(first: ValidatedM9Settings | null, second: ValidatedM9Settings): boolean {
  return first !== null &&
    first.capitalCopper === second.capitalCopper &&
    first.riskProfile === second.riskProfile;
}

export default function App() {
  const [route, setRoute] = useState<M9Route | null>(() => getRoute(window.location.pathname));
  const [settings, setSettings] = useState<ValidatedM9Settings | null>(() => loadSettings());
  const [pendingSettingsChange, setPendingSettingsChange] = useState<PendingSettingsChange | null>(null);
  const [isConfirmingSettingsChange, setIsConfirmingSettingsChange] = useState(false);
  const [settingsChangeError, setSettingsChangeError] = useState<string | null>(null);
  const scan = usePlayerScan();

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

  function commitSettings(nextSettings: ValidatedM9Settings, settingsChanged: boolean): boolean {
    try {
      saveSettings(nextSettings);
    } catch {
      return false;
    }

    if (settingsChanged) scan.discard();
    setSettings(nextSettings);
    navigate('recommendations');
    return true;
  }

  function requestSettingsSave(nextSettings: ValidatedM9Settings): boolean {
    const settingsChanged = !settingsMatch(settings, nextSettings);
    const hasCompletedResult = scan.session.snapshot.state === 'complete' && scan.session.snapshot.result !== null;
    if (settingsChanged && (hasCompletedResult || scan.isActive)) {
      setSettingsChangeError(null);
      setPendingSettingsChange({ settings: nextSettings });
      return true;
    }

    return commitSettings(nextSettings, settingsChanged);
  }

  async function confirmSettingsChange() {
    if (pendingSettingsChange === null) return;

    setSettingsChangeError(null);
    setIsConfirmingSettingsChange(true);
    if (scan.isActive && !await scan.cancelForSettingsChange()) {
      setSettingsChangeError('The active scan could not be cancelled safely. Your current settings were kept.');
      setIsConfirmingSettingsChange(false);
      return;
    }

    if (!commitSettings(pendingSettingsChange.settings, true)) {
      setSettingsChangeError('Your settings could not be saved in this browser. Please try again.');
      setIsConfirmingSettingsChange(false);
      return;
    }

    setPendingSettingsChange(null);
    setIsConfirmingSettingsChange(false);
  }

  function dismissSettingsChange() {
    if (isConfirmingSettingsChange) return;
    setPendingSettingsChange(null);
    setSettingsChangeError(null);
    window.setTimeout(() => document.getElementById('save-settings')?.focus());
  }

  async function resetTutorial(): Promise<TutorialResetResult> {
    if (scan.isActive && !await scan.cancelForSettingsChange()) return 'cancellation-failed';
    if (!clearSettings()) return 'storage-failed';

    scan.discard();
    setSettings(null);
    navigate('recommendations');
    return 'reset';
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
        ? <RecommendationsPage scan={scan} settings={settings} onOpenSettings={() => navigate('settings')} />
        : <SettingsPage settings={settings} onSave={requestSettingsSave} onResetTutorial={resetTutorial} />}
      {pendingSettingsChange !== null && <SettingsChangeDialog
        error={settingsChangeError}
        isBusy={isConfirmingSettingsChange}
        isCancellingScan={scan.isActive}
        onCancel={dismissSettingsChange}
        onConfirm={() => void confirmSettingsChange()}
      />}
    </main>
  );
}

function SettingsPage({
  settings,
  onSave,
  onResetTutorial,
}: {
  settings: ValidatedM9Settings | null;
  onSave: (settings: ValidatedM9Settings) => boolean;
  onResetTutorial: () => Promise<TutorialResetResult>;
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

    if (!onSave(validation.settings)) {
      setSaveError('Your settings could not be saved in this browser. Please try again.');
    }
  }

  async function handleResetTutorial() {
    setSaveError(null);
    setResetError(null);
    const result = await onResetTutorial();
    if (result === 'cancellation-failed') {
      setResetError('The active scan could not be cancelled safely. Your current settings were kept.');
    } else if (result === 'storage-failed') {
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
          <button className="secondary-action" onClick={() => void handleResetTutorial()} type="button">Reset tutorial</button>
        </div>
        <p className="field-help reset-tutorial-help">This clears only your saved capital and risk on this device, then returns you to the tutorial.</p>
      </form>
    </section>
  );
}

function SettingsChangeDialog({
  error,
  isBusy,
  isCancellingScan,
  onCancel,
  onConfirm,
}: {
  error: string | null;
  isBusy: boolean;
  isCancellingScan: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const cancelButtonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (isBusy) dialogRef.current?.focus();
    else cancelButtonRef.current?.focus();
  }, [isBusy]);

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape' && !isBusy) {
      event.preventDefault();
      onCancel();
      return;
    }

    if (event.key !== 'Tab') return;
    if (isBusy) {
      event.preventDefault();
      dialogRef.current?.focus();
      return;
    }
    const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? []);
    if (focusable.length === 0) return;

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

  const confirmationLabel = isCancellingScan ? 'Cancel scan and save settings' : 'Save new settings and clear scan';
  const description = isCancellingScan
    ? 'Changing capital or risk will cancel the active scan before the new settings are saved. No recommendations from that scan will be kept.'
    : 'Changing capital or risk will remove the current scan result. You can scan the market again with the new settings.';

  return (
    <div className="modal-backdrop">
      <div aria-describedby="settings-change-description" aria-labelledby="settings-change-title" aria-modal="true" className="confirmation-dialog" onKeyDown={handleKeyDown} ref={dialogRef} role="alertdialog" tabIndex={-1}>
        <p className="eyebrow">Confirm settings change</p>
        <h2 id="settings-change-title">Clear the current scan?</h2>
        <p id="settings-change-description">{description}</p>
        {error !== null && <p className="field-error" role="alert">{error}</p>}
        <div className="dialog-actions">
          <button className="secondary-action" disabled={isBusy} onClick={onCancel} ref={cancelButtonRef} type="button">Keep current settings</button>
          <button className="primary-action" disabled={isBusy} onClick={onConfirm} type="button">{isBusy ? 'Cancelling scan…' : confirmationLabel}</button>
        </div>
      </div>
    </div>
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
  scan,
}: {
  settings: ValidatedM9Settings | null;
  onOpenSettings: () => void;
  scan: PlayerScanController;
}) {
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
  const { snapshot, isStarting, isCancelling, requestError } = scan.session;
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
        <button className="primary-action" disabled={isStarting || isCancelling || snapshot.state === 'running'} onClick={() => void scan.start(settings)} type="button">{isStarting ? 'Starting scan…' : 'Scan the market'}</button>
        {snapshot.state === 'running' && !isStarting && <button className="secondary-action" disabled={isCancelling} onClick={() => void scan.cancel()} type="button">{isCancelling ? 'Cancelling scan…' : 'Cancel scan'}</button>}
      </div>

      <ScanStateNotice isCancelling={isCancelling} isStarting={isStarting} onRetry={() => void scan.start(settings)} requestError={requestError} snapshot={snapshot} />
      {snapshot.state === 'complete' && snapshot.result !== null && <RecommendationResults result={snapshot.result} />}
    </section>
  );
}

function usePlayerScan(): PlayerScanController {
  const [session, setScanSession] = useState<ScanSession>(idleScanSession);
  const sessionRef = useRef(session);
  const generationRef = useRef(0);
  const startTaskRef = useRef<Promise<ScanSnapshot> | null>(null);

  const updateSession = useCallback((next: SetStateAction<ScanSession>) => {
    setScanSession((current) => {
      const resolved = typeof next === 'function'
        ? (next as (current: ScanSession) => ScanSession)(current)
        : next;
      sessionRef.current = resolved;
      return resolved;
    });
  }, []);

  const discard = useCallback(() => {
    generationRef.current += 1;
    updateSession(idleScanSession);
  }, [updateSession]);

  useEffect(() => () => {
    generationRef.current += 1;
  }, []);

  useEffect(() => {
    if (session.snapshot.state !== 'running' || session.isStarting || session.isCancelling) return;

    let active = true;
    let timer: number | undefined;
    const generation = generationRef.current;
    const poll = async () => {
      try {
        const next = safeSnapshot(await getScanStatus());
        if (!active || generation !== generationRef.current) return;
        updateSession((current) => ({ ...current, snapshot: next, requestError: null }));
        if (next.state === 'running') timer = window.setTimeout(() => void poll(), SCAN_STATUS_POLL_INTERVAL_MS);
      } catch {
        if (!active || generation !== generationRef.current) return;
        updateSession({
          snapshot: { state: 'failed', progress: null, isRetryable: true, result: null },
          isStarting: false,
          isCancelling: false,
          requestError: 'The scan status could not be checked. No recommendations were kept.',
        });
      }
    };

    timer = window.setTimeout(() => void poll(), SCAN_STATUS_POLL_INTERVAL_MS);
    return () => {
      active = false;
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [session.isCancelling, session.isStarting, session.snapshot.state, updateSession]);

  const start = useCallback(async (settings: ValidatedM9Settings) => {
    const current = sessionRef.current;
    if (current.isStarting || current.isCancelling || current.snapshot.state === 'running') return;

    const generation = generationRef.current + 1;
    generationRef.current = generation;
    updateSession({ ...idleScanSession, isStarting: true });
    const task = startScan(settings).then(safeSnapshot);
    startTaskRef.current = task;
    try {
      const next = await task;
      if (generation !== generationRef.current) return;
      updateSession({ snapshot: next, isStarting: false, isCancelling: false, requestError: null });
    } catch {
      if (generation !== generationRef.current) return;
      updateSession({
        snapshot: { state: 'failed', progress: null, isRetryable: true, result: null },
        isStarting: false,
        isCancelling: false,
        requestError: 'The scan could not be started. No recommendations were kept.',
      });
    } finally {
      if (startTaskRef.current === task) startTaskRef.current = null;
    }
  }, [updateSession]);

  const cancel = useCallback(async () => {
    if (sessionRef.current.snapshot.state !== 'running' || sessionRef.current.isStarting || sessionRef.current.isCancelling) return;

    const generation = generationRef.current + 1;
    generationRef.current = generation;
    updateSession((current) => ({
      ...current,
      snapshot: { ...current.snapshot, result: null },
      isCancelling: true,
      requestError: null,
    }));
    try {
      const next = safeCancelledSnapshot(await cancelScan());
      if (generation !== generationRef.current) return;
      updateSession({ snapshot: next, isStarting: false, isCancelling: false, requestError: null });
    } catch {
      if (generation !== generationRef.current) return;
      updateSession({
        snapshot: { state: 'failed', progress: null, isRetryable: true, result: null },
        isStarting: false,
        isCancelling: false,
        requestError: 'The scan could not be cancelled safely. No recommendations were kept.',
      });
    }
  }, [updateSession]);

  const cancelForSettingsChange = useCallback(async (): Promise<boolean> => {
    const source = sessionRef.current;
    if (!source.isStarting && source.snapshot.state !== 'running') return true;

    const generation = generationRef.current + 1;
    generationRef.current = generation;
    updateSession((current) => ({
      ...current,
      snapshot: { ...current.snapshot, result: null },
      isCancelling: true,
      requestError: null,
    }));

    const markCancellationFailure = () => {
      if (generation === generationRef.current) {
        updateSession({
          snapshot: { state: 'failed', progress: null, isRetryable: true, result: null },
          isStarting: false,
          isCancelling: false,
          requestError: 'The active scan could not be cancelled safely. Your settings were not changed.',
        });
      }
    };

    const startTask = startTaskRef.current;
    let startedSnapshot: ScanSnapshot | null = null;
    if (source.isStarting && startTask === null) {
      markCancellationFailure();
      return false;
    }

    if (startTask !== null) {
      try {
        startedSnapshot = await startTask;
      } catch {
        try {
          await cancelScan();
        } catch {
          // The original request is still ambiguous either way, so settings stay unchanged.
        }
        markCancellationFailure();
        return false;
      }
    }

    const needsCancellation = (startedSnapshot ?? source.snapshot).state === 'running';
    if (needsCancellation) {
      try {
        const terminalSnapshot = safeSnapshot(await cancelScan());
        if (terminalSnapshot.state === 'running') throw new Error('Scan is still active.');
      } catch {
        markCancellationFailure();
        return false;
      }
    }

    if (generation === generationRef.current) updateSession(idleScanSession);
    return true;
  }, [updateSession]);

  return {
    session,
    isActive: session.isStarting || session.isCancelling || session.snapshot.state === 'running',
    start,
    cancel,
    discard,
    cancelForSettingsChange,
  };
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
  if (finalistCount === null) return base;
  return `${base} ${finalistCount} finalist${finalistCount === 1 ? ' needs' : 's need'} detailed checks.`;
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
    'current-order-book-depth-and-spread-guard': 'Passed fixed current order-book depth and relative-spread checks. This does not guarantee a fill or sale.',
    'manual-in-game-orders-required': 'Every Trading Post action remains manual.',
    'no-execution-sale-or-profit-guarantee': 'No execution, sale, or profit is guaranteed.',
    'fee-rounding-pending-external-verification': 'Fee rounding is a current model assumption.',
  };
  return messages[assumption] ?? 'Uses the current public market data available to this scan.';
}
