import { useEffect, useRef, useState, type ReactNode } from 'react';
import './App.css';
import RecommendationPanel from './RecommendationPanel';
import MoneyDisplay from './MoneyDisplay';

type HostStatus = 'checking' | 'connected' | 'unavailable';
type AccountConnectionState =
  | 'checking'
  | 'not_configured'
  | 'valid'
  | 'invalid'
  | 'insufficient_permissions'
  | 'unavailable';

type AccountConnectionResponse = {
  state: Exclude<AccountConnectionState, 'checking'>;
  grantedPermissions: string[];
  missingRequiredPermissions: string[];
};

type LocalDataLocation = {
  databasePath: string;
  backupDirectoryPath: string;
  managedBackupUploadLimitBytes?: number;
  managedBackups?: Array<{ fileName: string; createdAtUtc: string }>;
};

type LocalDataLocationState =
  | { kind: 'loading' }
  | { kind: 'unavailable' }
  | { kind: 'ready'; location: LocalDataLocation };

type Money = { copper: string };
type Dashboard = {
  state: 'ready' | 'notSynchronized' | 'accountUnavailable';
  accountError: string | null;
  lastSuccessfulSyncAtUtc: string | null;
  currentOrdersObservedAtUtc: string | null;
  historyCoverage: { startUtc: string | null; endUtc: string | null } | null;
  marketState: 'available' | 'unavailable';
  isFeeRoundingExternallyVerified: boolean;
  realizedWindows: Array<{ days: number; status: 'supported' | 'insufficientCoverage'; netProfit: Money | null; unknownBasisQuantity: number }>;
  todayRealized: { days: number; status: 'supported' | 'insufficientCoverage'; netProfit: Money | null; unknownBasisQuantity: number } | null;
  openAcquisitionBasis: Money | null;
  netLiquidationValue: Money | null;
  unrealizedProfit: Money | null;
  isOpenInventoryFullyValued: boolean | null;
  openInventory: Array<{ itemId: number; itemName: string; quantity: number; acquisitionBasis: Money; liquidationStatus: string; unliquidatedQuantity: number; netLiquidationValue: Money | null; unrealizedProfit: Money | null }>;
  currentBuyCapital: Money;
  currentSellGrossValue: Money;
  currentSellNetValue: Money;
  currentOrders: Array<{ orderId: string; side: 'buy' | 'sell'; itemId: number; itemName: string; quantity: number; unitPrice: Money; marketComparisonStatus: 'available' | 'missingSide' | 'unavailable'; currentMarketUnitPrice: Money | null }>;
  recentTrades: Array<{ transactionId: string; side: 'buy' | 'sell'; itemName: string; quantity: number; unitPrice: Money; completedAtUtc: string }>;
  bestRealizedItems: Array<{ itemId: number; itemName: string; quantity: number; netProfit: Money }>;
  worstRealizedItems: Array<{ itemId: number; itemName: string; quantity: number; netProfit: Money }>;
  personalLearning: {
    status: 'insufficientCoverage' | 'insufficientSamples' | 'insufficientMetrics' | 'stale' | 'supported';
    timestampLimitation: string;
    minimumKnownBasisSamples: number;
    exactSourceTimestampCount: number;
    intervalCensoredCompletionCount: number;
    unknownOrderTimingCount: number;
    observedQuantityReductionCount: number;
    fillTiming: Array<{ side: 'buy' | 'sell'; exactSourceTimestampCount: number; averageSourceDuration: string | null; intervalCensoredCompletionCount: number; averageConfirmationWindow: string | null }>;
    items: Array<{ itemId: number; itemName: string; status: 'insufficientCoverage' | 'insufficientSamples' | 'insufficientMetrics' | 'stale' | 'supported'; exactSourceTimestampCount: number; intervalCensoredCompletionCount: number; unknownOrderTimingCount: number; observedQuantityReductionCount: number; knownBasisSampleCount: number | null; averageHoldingDuration: string | null; realizedProfitPerDay: { numerator: string; denominator: string } | null; capitalTurns: { numerator: string; denominator: string } | null }>;
    knownBasisSampleCount: number | null;
    latestKnownBasisCompletionAtUtc: string | null;
    netProfit: Money | null;
    matchedAcquisitionBasis: Money | null;
    averageHoldingDuration: string | null;
    realizedProfitPerDay: { numerator: string; denominator: string } | null;
    capitalTurns: { numerator: string; denominator: string } | null;
  } | null;
};

function isAccountConnectionResponse(payload: unknown): payload is AccountConnectionResponse {
  if (typeof payload !== 'object' || payload === null) {
    return false;
  }

  const candidate = payload as Record<string, unknown>;
  return ['not_configured', 'valid', 'invalid', 'insufficient_permissions', 'unavailable'].includes(candidate.state as string)
    && Array.isArray(candidate.grantedPermissions)
    && Array.isArray(candidate.missingRequiredPermissions)
    && candidate.grantedPermissions.every((permission) => typeof permission === 'string')
    && candidate.missingRequiredPermissions.every((permission) => typeof permission === 'string');
}

function accountConnectionMessage(state: AccountConnectionState, missingPermissions: string[]): string {
  switch (state) {
    case 'checking':
      return 'Vérification de la clé ArenaNet…';
    case 'not_configured':
      return 'Aucune clé ArenaNet configurée';
    case 'valid':
      return 'Connexion au compte prête';
    case 'invalid':
      return 'La clé ArenaNet est invalide ou révoquée';
    case 'insufficient_permissions':
      return `Autorisation ArenaNet insuffisante : ${missingPermissions.join(', ')}`;
    case 'unavailable':
      return 'État de la clé ArenaNet indisponible';
  }
}

function isLocalDataLocation(payload: unknown): payload is LocalDataLocation {
  if (typeof payload !== 'object' || payload === null) {
    return false;
  }

  const candidate = payload as Record<string, unknown>;
  return typeof candidate.databasePath === 'string'
    && typeof candidate.backupDirectoryPath === 'string'
    && (candidate.managedBackupUploadLimitBytes === undefined
      || (typeof candidate.managedBackupUploadLimitBytes === 'number'
        && Number.isSafeInteger(candidate.managedBackupUploadLimitBytes)
        && candidate.managedBackupUploadLimitBytes > 0))
    && (candidate.managedBackups === undefined
      || (Array.isArray(candidate.managedBackups)
        && candidate.managedBackups.every((backup) => typeof backup === 'object'
          && backup !== null
          && typeof (backup as Record<string, unknown>).fileName === 'string'
          && typeof (backup as Record<string, unknown>).createdAtUtc === 'string')));
}

function localRequestHeaders(): Record<string, string> {
  return {
    Accept: 'application/json',
    'X-Tyrian-Ledger-Request': '1',
  };
}

export default function App() {
  const [hostStatus, setHostStatus] = useState<HostStatus>('checking');
  const [accountConnection, setAccountConnection] = useState<{
    state: AccountConnectionState;
    missingPermissions: string[];
  }>({ state: 'checking', missingPermissions: [] });
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [dashboardStatus, setDashboardStatus] = useState<'loading' | 'error' | 'ready'>('loading');
  const [syncStatus, setSyncStatus] = useState<'idle' | 'syncing' | 'failed'>('idle');
  const [localDataRefreshGeneration, setLocalDataRefreshGeneration] = useState(0);
  const [activeView, setActiveView] = useState<'signals' | 'settings'>('signals');
  const dashboardRequestGeneration = useRef(0);

  useEffect(() => {
    const controller = new AbortController();

    async function checkHost() {
      try {
        const response = await fetch('/api/health', {
          headers: { Accept: 'application/json' },
          signal: controller.signal,
        });
        const payload: unknown = await response.json();
        const isHealthy = response.ok
          && typeof payload === 'object'
          && payload !== null
          && 'status' in payload
          && payload.status === 'healthy';

        setHostStatus(isHealthy ? 'connected' : 'unavailable');
      } catch (error) {
        if (!(error instanceof DOMException && error.name === 'AbortError')) {
          setHostStatus('unavailable');
        }
      }
    }

    void checkHost();
    return () => controller.abort();
  }, []);

  const loadDashboard = () => {
    const generation = ++dashboardRequestGeneration.current;
    setDashboardStatus('loading');
    void fetch('/api/personal-dashboard', { headers: localRequestHeaders() })
      .then(async (response) => {
        const payload: unknown = await response.json();
        if (generation !== dashboardRequestGeneration.current) {
          return;
        }

        if (!response.ok || !isDashboard(payload)) {
          setDashboardStatus('error');
          return;
        }
        setDashboard(payload);
        setDashboardStatus('ready');
      })
      .catch(() => {
        if (generation === dashboardRequestGeneration.current) {
          setDashboardStatus('error');
        }
      });
  };

  const refreshLocalDataViews = () => {
    loadDashboard();
    setLocalDataRefreshGeneration(generation => generation + 1);
  };

  useEffect(() => {
    loadDashboard();
    return () => { dashboardRequestGeneration.current++; };
  }, []);

  const synchronize = () => {
    setSyncStatus('syncing');
    void fetch('/api/personal-trading-post/sync', { method: 'POST', headers: localRequestHeaders() })
      .then(async (response) => {
        const payload: unknown = await response.json();
        if (!response.ok || typeof payload !== 'object' || payload === null || (payload as Record<string, unknown>).outcome !== 'succeeded') {
          setSyncStatus('failed');
          return;
        }
        setSyncStatus('idle');
        refreshLocalDataViews();
      })
      .catch(() => setSyncStatus('failed'));
  };

  useEffect(() => {
    const controller = new AbortController();

    async function checkAccountConnection() {
      try {
        const response = await fetch('/api/account-connection', {
          headers: {
            Accept: 'application/json',
            'X-Tyrian-Ledger-Request': '1',
          },
          signal: controller.signal,
        });
        const payload: unknown = await response.json();
        if (response.ok && isAccountConnectionResponse(payload)) {
          setAccountConnection({
            state: payload.state,
            missingPermissions: payload.missingRequiredPermissions,
          });
          return;
        }

        setAccountConnection({ state: 'unavailable', missingPermissions: [] });
      } catch (error) {
        if (!(error instanceof DOMException && error.name === 'AbortError')) {
          setAccountConnection({ state: 'unavailable', missingPermissions: [] });
        }
      }
    }

    void checkAccountConnection();
    return () => controller.abort();
  }, []);

  return (
    <div className="signals-app">
      <a className="skip-link" href="#main-content">Aller au contenu principal</a>
      <aside className="signals-sidebar">
        <div aria-label="Tyrian Ledger" className="brand-lockup">
          <span aria-hidden="true" className="brand-mark">TL</span>
          <span><strong>Tyrian Ledger</strong><small>Assistant de profit</small></span>
        </div>
        <nav aria-label="Navigation principale" className="primary-navigation">
          <button
            aria-current={activeView === 'signals' ? 'page' : undefined}
            className={activeView === 'signals' ? 'nav-item nav-item--active' : 'nav-item'}
            onClick={() => setActiveView('signals')}
            type="button"
          >
            <span aria-hidden="true">◆</span>
            <span>Mes Signaux</span>
          </button>
          <button className="nav-item" disabled type="button">
            <span aria-hidden="true">◇</span>
            <span>Artisanat <small>Bientôt</small></span>
          </button>
          <button
            aria-current={activeView === 'settings' ? 'page' : undefined}
            className={activeView === 'settings' ? 'nav-item nav-item--active' : 'nav-item'}
            onClick={() => setActiveView('settings')}
            type="button"
          >
            <span aria-hidden="true">⚙</span>
            <span>Réglages</span>
          </button>
        </nav>
        <div className="sidebar-status">
          <span className={`host-dot host-dot--${hostStatus}`} aria-hidden="true" />
          {hostStatus === 'checking' && 'Application locale…'}
          {hostStatus === 'connected' && 'Application locale connectée'}
          {hostStatus === 'unavailable' && 'Application locale indisponible'}
        </div>
      </aside>

      <main className="signals-main" id="main-content">
        {activeView === 'signals' ? (
          <>
            <header className="signals-header">
              <div>
                <p className="eyebrow">Assistant d'action</p>
                <h1>Mes Signaux</h1>
                <p className="page-introduction">Uniquement les actions qui méritent votre attention maintenant.</p>
              </div>
              <PerformanceSummary dashboard={dashboard} status={dashboardStatus} />
            </header>
            <RecommendationPanel refreshGeneration={localDataRefreshGeneration} />
          </>
        ) : (
          <section aria-labelledby="settings-title" className="settings-view">
            <header className="settings-header">
              <p className="eyebrow">Configuration locale</p>
              <h1 id="settings-title">Réglages</h1>
              <p>Connexion ArenaNet, synchronisation et gestion des données locales.</p>
            </header>

            <section aria-labelledby="account-connection-title" className="account-connection-panel">
              <p className="eyebrow">Compte ArenaNet</p>
              <h2 id="account-connection-title">Connexion en lecture seule</h2>
              <p aria-live="polite" className={`account-connection-status account-connection-status--${accountConnection.state}`} role="status">
                <span aria-hidden="true" />
                {accountConnectionMessage(accountConnection.state, accountConnection.missingPermissions)}
              </p>
              {(accountConnection.state === 'not_configured' || accountConnection.state === 'unavailable') && (
                <p>Enregistrez une clé ArenaNet dédiée et en lecture seule dans le coffre d'identifiants du système. Le navigateur ne stocke jamais la clé.</p>
              )}
              {accountConnection.state === 'insufficient_permissions' && (
                <p>La clé doit autoriser account, tradingpost et wallet pour les recommandations personnelles en lecture seule.</p>
              )}
              <button className="sync-button" disabled={syncStatus === 'syncing' || accountConnection.state !== 'valid'} onClick={synchronize} type="button">
                {syncStatus === 'syncing' ? 'Synchronisation en cours…' : 'Synchroniser les données du Comptoir'}
              </button>
              {syncStatus === 'failed' && <p role="alert">La synchronisation n'a pas pu être confirmée. Les données locales existantes sont conservées.</p>}
            </section>

            <LocalDataPanel onPersonalDataChanged={refreshLocalDataViews} />

            <section className="legal-notice">
              <h2>À propos</h2>
              <p>Tyrian Ledger est un projet communautaire indépendant et non officiel pour Guild Wars 2. Il n'est ni affilié à ArenaNet ou NCSOFT, ni approuvé par eux.</p>
              <p>Guild Wars 2 © ArenaNet, LLC. Tous droits réservés. Guild Wars 2 et GW2 sont des marques de NCSOFT Corporation.</p>
            </section>
          </section>
        )}
      </main>
    </div>
  );
}

function PerformanceSummary({ dashboard, status }: { dashboard: Dashboard | null; status: 'loading' | 'error' | 'ready' }) {
  const windowFor = (days: number) => dashboard?.realizedWindows.find(window => window.days === days) ?? null;
  const today = dashboard?.todayRealized ?? null;
  const thirty = windowFor(30);
  const seven = windowFor(7);
  const ninety = windowFor(90);
  const value = (window: { status: 'supported' | 'insufficientCoverage'; netProfit: Money | null } | null) =>
    window?.status === 'supported' ? window.netProfit : null;
  const unavailable = status === 'loading' ? 'Chargement…' : 'Couverture insuffisante';

  return (
    <section aria-label="Profit réalisé" className="performance-summary">
      <span className="performance-title">Profit réalisé</span>
      <div className="performance-primary">
        <div><span>Aujourd'hui</span>{value(today) ? <MoneyDisplay compact money={value(today)} /> : <strong>{unavailable}</strong>}</div>
        <div><span>30 j</span>{value(thirty) ? <MoneyDisplay compact money={value(thirty)} /> : <strong>{unavailable}</strong>}</div>
      </div>
      <details>
        <summary>7 j / 90 j</summary>
        <div className="performance-secondary">
          <div><span>7 jours</span>{value(seven) ? <MoneyDisplay compact money={value(seven)} /> : <strong>{unavailable}</strong>}</div>
          <div><span>90 jours</span>{value(ninety) ? <MoneyDisplay compact money={value(ninety)} /> : <strong>{unavailable}</strong>}</div>
        </div>
      </details>
    </section>
  );
}

function isDashboard(payload: unknown): payload is Dashboard {
  if (!isRecord(payload)) return false;
  return isOneOf(payload.state, ['ready', 'notSynchronized', 'accountUnavailable'])
    && isNullableString(payload.accountError)
    && isNullableString(payload.lastSuccessfulSyncAtUtc)
    && isNullableString(payload.currentOrdersObservedAtUtc)
    && isNullableHistoryCoverage(payload.historyCoverage)
    && isOneOf(payload.marketState, ['available', 'unavailable'])
    && typeof payload.isFeeRoundingExternallyVerified === 'boolean'
    && isArrayOf(payload.realizedWindows, isRealizedWindow)
    && (payload.todayRealized === null || payload.todayRealized === undefined || isRealizedWindow(payload.todayRealized))
    && isNullableMoney(payload.openAcquisitionBasis)
    && isNullableMoney(payload.netLiquidationValue)
    && isNullableMoney(payload.unrealizedProfit)
    && (typeof payload.isOpenInventoryFullyValued === 'boolean' || payload.isOpenInventoryFullyValued === null)
    && isArrayOf(payload.openInventory, isOpenInventory)
    && isMoney(payload.currentBuyCapital)
    && isMoney(payload.currentSellGrossValue)
    && isMoney(payload.currentSellNetValue)
    && isArrayOf(payload.currentOrders, isCurrentOrder)
    && isArrayOf(payload.recentTrades, isRecentTrade)
    && isArrayOf(payload.bestRealizedItems, isRealizedItem)
    && isArrayOf(payload.worstRealizedItems, isRealizedItem)
    && (payload.personalLearning === null || isPersonalLearning(payload.personalLearning));
}

function copper(money: Money | null): string {
  if (money === null) return 'Unavailable';
  const value = BigInt(money.copper);
  const sign = value < 0n ? '−' : '';
  const absolute = value < 0n ? -value : value;
  return `${sign}${absolute / 10000n}g ${(absolute % 10000n) / 100n}s ${absolute % 100n}c`;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isOneOf<T extends string>(value: unknown, values: readonly T[]): value is T {
  return typeof value === 'string' && values.includes(value as T);
}

function isNonNegativeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}

function isDecimalIdentifier(value: unknown): value is string {
  return typeof value === 'string' && /^\d+$/.test(value);
}

function isMoney(value: unknown): value is Money {
  return isRecord(value) && typeof value.copper === 'string' && /^-?\d+$/.test(value.copper);
}

function isNullableMoney(value: unknown): value is Money | null {
  return value === null || isMoney(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

function isArrayOf(value: unknown, item: (candidate: unknown) => boolean): value is unknown[] {
  return Array.isArray(value) && value.every(item);
}

function isNullableHistoryCoverage(value: unknown): boolean {
  return value === null || (isRecord(value) && isNullableString(value.startUtc) && isNullableString(value.endUtc));
}

function isRealizedWindow(value: unknown): boolean {
  return isRecord(value)
    && isNonNegativeInteger(value.days)
    && isOneOf(value.status, ['supported', 'insufficientCoverage'])
    && isNullableMoney(value.netProfit)
    && isNonNegativeInteger(value.unknownBasisQuantity);
}

function isOpenInventory(value: unknown): boolean {
  return isRecord(value)
    && isNonNegativeInteger(value.itemId)
    && typeof value.itemName === 'string'
    && isNonNegativeInteger(value.quantity)
    && isMoney(value.acquisitionBasis)
    && isOneOf(value.liquidationStatus, ['fullyValued', 'insufficientBuyDepth', 'evidenceMissing'])
    && isNonNegativeInteger(value.unliquidatedQuantity)
    && isNullableMoney(value.netLiquidationValue)
    && isNullableMoney(value.unrealizedProfit);
}

function isCurrentOrder(value: unknown): boolean {
  return isRecord(value)
    && isDecimalIdentifier(value.orderId)
    && isOneOf(value.side, ['buy', 'sell'])
    && isNonNegativeInteger(value.itemId)
    && typeof value.itemName === 'string'
    && isNonNegativeInteger(value.quantity)
    && isMoney(value.unitPrice)
    && isOneOf(value.marketComparisonStatus, ['available', 'missingSide', 'unavailable'])
    && isNullableMoney(value.currentMarketUnitPrice);
}

function isRecentTrade(value: unknown): boolean {
  return isRecord(value)
    && isDecimalIdentifier(value.transactionId)
    && isOneOf(value.side, ['buy', 'sell'])
    && typeof value.itemName === 'string'
    && isNonNegativeInteger(value.quantity)
    && isMoney(value.unitPrice)
    && typeof value.completedAtUtc === 'string';
}

function isRealizedItem(value: unknown): boolean {
  return isRecord(value)
    && isNonNegativeInteger(value.itemId)
    && typeof value.itemName === 'string'
    && isNonNegativeInteger(value.quantity)
    && isMoney(value.netProfit);
}

function isExactRate(value: unknown): boolean {
  return isRecord(value)
    && typeof value.numerator === 'string'
    && typeof value.denominator === 'string'
    && /^-?\d+$/.test(value.numerator)
    && /^\d+$/.test(value.denominator)
    && BigInt(value.denominator) > 0n;
}

function isPersonalLearning(value: unknown): boolean {
  return isRecord(value)
    && isOneOf(value.status, ['insufficientCoverage', 'insufficientSamples', 'insufficientMetrics', 'stale', 'supported'])
    && typeof value.timestampLimitation === 'string'
    && isNonNegativeInteger(value.minimumKnownBasisSamples)
    && isNonNegativeInteger(value.exactSourceTimestampCount)
    && isNonNegativeInteger(value.intervalCensoredCompletionCount)
    && isNonNegativeInteger(value.unknownOrderTimingCount)
    && isNonNegativeInteger(value.observedQuantityReductionCount)
    && isArrayOf(value.fillTiming, isFillTiming)
    && isArrayOf(value.items, isPersonalLearningItem)
    && (value.knownBasisSampleCount === null || isNonNegativeInteger(value.knownBasisSampleCount))
    && isNullableString(value.latestKnownBasisCompletionAtUtc)
    && isNullableMoney(value.netProfit)
    && isNullableMoney(value.matchedAcquisitionBasis)
    && isNullableString(value.averageHoldingDuration)
    && (value.realizedProfitPerDay === null || isExactRate(value.realizedProfitPerDay))
    && (value.capitalTurns === null || isExactRate(value.capitalTurns));
}

function isFillTiming(value: unknown): boolean {
  return isRecord(value)
    && isOneOf(value.side, ['buy', 'sell'])
    && isNonNegativeInteger(value.exactSourceTimestampCount)
    && isNullableString(value.averageSourceDuration)
    && isNonNegativeInteger(value.intervalCensoredCompletionCount)
    && isNullableString(value.averageConfirmationWindow);
}

function isPersonalLearningItem(value: unknown): boolean {
  return isRecord(value)
    && isNonNegativeInteger(value.itemId)
    && typeof value.itemName === 'string'
    && isOneOf(value.status, ['insufficientCoverage', 'insufficientSamples', 'insufficientMetrics', 'stale', 'supported'])
    && isNonNegativeInteger(value.exactSourceTimestampCount)
    && isNonNegativeInteger(value.intervalCensoredCompletionCount)
    && isNonNegativeInteger(value.unknownOrderTimingCount)
    && isNonNegativeInteger(value.observedQuantityReductionCount)
    && (value.knownBasisSampleCount === null || isNonNegativeInteger(value.knownBasisSampleCount))
    && isNullableString(value.averageHoldingDuration)
    && (value.realizedProfitPerDay === null || isExactRate(value.realizedProfitPerDay))
    && (value.capitalTurns === null || isExactRate(value.capitalTurns));
}

function exactRate(value: { numerator: string; denominator: string } | null, suffix: string): string {
  if (value === null) return 'Unavailable';
  const numerator = BigInt(value.numerator);
  const denominator = BigInt(value.denominator);
  const negative = numerator < 0n;
  const scaled = (negative ? -numerator : numerator) * 100n / denominator;
  return `${negative ? '−' : ''}${scaled / 100n}.${(scaled % 100n).toString().padStart(2, '0')} ${suffix}`;
}

function personalLearningStatus(status: NonNullable<Dashboard['personalLearning']>['status']): string {
  switch (status) {
    case 'supported': return 'Sufficient recent known-basis evidence';
    case 'stale': return 'Evidence is stale; it is not strong current evidence';
    case 'insufficientSamples': return 'Too few known-basis outcomes for strong evidence';
    case 'insufficientMetrics': return 'Turnover metrics cannot be calculated from the retained outcomes';
    case 'insufficientCoverage': return 'Continuous completed-history coverage is not available';
  }
}

function timestamp(value: string | null): string {
  return value === null ? 'Not yet recorded' : new Date(value).toLocaleString();
}

function DashboardPanel({ dashboard, status }: { dashboard: Dashboard | null; status: 'loading' | 'error' | 'ready' }) {
  if (status === 'loading') return <section className="dashboard-panel" aria-busy="true"><h2>Loading personal dashboard…</h2></section>;
  if (status === 'error' || dashboard === null) return <section className="dashboard-panel" role="alert"><h2>Dashboard unavailable</h2><p>The local dashboard could not be read. Check the local host and try again.</p></section>;
  if (dashboard.state === 'accountUnavailable') return <section className="dashboard-panel"><h2>Connect an account to view your dashboard</h2><p>Account access is unavailable ({dashboard.accountError ?? 'unknown error'}). Your browser never receives the key.</p></section>;
  if (dashboard.state === 'notSynchronized') return <section className="dashboard-panel"><h2>No personal data yet</h2><p>Synchronize a valid Trading Post account to create the first local snapshot.</p></section>;

  const coverage = dashboard.historyCoverage;
  return <section className="dashboard-panel" aria-labelledby="performance-title">
    <div className="dashboard-heading"><div><p className="eyebrow">Retained evidence</p><h2 id="performance-title">Performance and current orders</h2></div><p className="sync-time">Last sync: {timestamp(dashboard.lastSuccessfulSyncAtUtc)}</p></div>
    {coverage === null || coverage.startUtc === null || coverage.endUtc === null ? <p className="notice">Continuous history coverage is not available. No realized performance claim is shown.</p> : <p className="notice">History coverage: {timestamp(coverage.startUtc)} to {timestamp(coverage.endUtc)}.</p>}
    {!dashboard.isFeeRoundingExternallyVerified && <p className="notice">Fee-derived values use the current modeled rounding policy and remain provisional.</p>}
    <div className="metric-grid">
      {dashboard.realizedWindows.map((window) => <section key={window.days}><h3>{window.days}-day realized P&amp;L</h3><strong>{window.status === 'supported' ? copper(window.netProfit) : 'Insufficient coverage'}</strong>{window.unknownBasisQuantity > 0 && <p>{window.unknownBasisQuantity} sold without known basis, excluded.</p>}</section>)}
      <section><h3>Open acquisition basis</h3><strong>{copper(dashboard.openAcquisitionBasis)}</strong></section>
      <section><h3>Unrealized P&amp;L</h3><strong>{dashboard.isOpenInventoryFullyValued ? copper(dashboard.unrealizedProfit) : 'Not fully valued'}</strong></section>
      <section><h3>Capital in buy orders</h3><strong>{copper(dashboard.currentBuyCapital)}</strong></section>
      <section><h3>Current sell listings</h3><strong>{copper(dashboard.currentSellGrossValue)} gross</strong><p>{copper(dashboard.currentSellNetValue)} modeled net</p></section>
    </div>
    {dashboard.personalLearning !== null && <section className="dashboard-learning" aria-labelledby="learning-title">
      <div><p className="eyebrow">Personal learning</p><h3 id="learning-title">Fill time and capital turnover</h3></div>
      <p className="notice">{dashboard.personalLearning.timestampLimitation}</p>
      <p aria-live="polite"><strong>{personalLearningStatus(dashboard.personalLearning.status)}</strong> {dashboard.personalLearning.knownBasisSampleCount ?? 0} portfolio-wide known-basis completed outcome{(dashboard.personalLearning.knownBasisSampleCount ?? 0) === 1 ? '' : 's'} retained. Each market requires {dashboard.personalLearning.minimumKnownBasisSamples} outcomes for strong evidence.</p>
      <div className="metric-grid">
        {dashboard.personalLearning.fillTiming.map((timing) => <section key={timing.side}><h3>{timing.side === 'buy' ? 'Buy' : 'Sell'} timing</h3><strong>{timing.averageSourceDuration ?? 'Unavailable'}</strong><p>{timing.exactSourceTimestampCount} source-timestamp duration{timing.exactSourceTimestampCount === 1 ? '' : 's'}; {timing.intervalCensoredCompletionCount} local confirmation window{timing.intervalCensoredCompletionCount === 1 ? '' : 's'} ({timing.averageConfirmationWindow ?? 'unavailable'} average).</p></section>)}
        <section><h3>Local confirmation windows</h3><strong>{dashboard.personalLearning.intervalCensoredCompletionCount}</strong><p>Polling supplies bounds only; no window claims an exact fill time.</p></section>
        <section><h3>Observed quantity reductions</h3><strong>{dashboard.personalLearning.observedQuantityReductionCount}</strong><p>Independent snapshot evidence; it does not claim a completed fill.</p></section>
        <section><h3>Unknown order timing</h3><strong>{dashboard.personalLearning.unknownOrderTimingCount}</strong><p>Disappearance from polling is never counted as a fill.</p></section>
        <section><h3>Average capital lock</h3><strong>{dashboard.personalLearning.averageHoldingDuration ?? 'Unavailable'}</strong><p>Known FIFO acquisition to completed sale.</p></section>
        <section><h3>Realized profit/day</h3><strong>{exactRate(dashboard.personalLearning.realizedProfitPerDay, 'c/day')}</strong><p>Exact retained-evidence ratio, displayed to two truncated decimals.</p></section>
        <section><h3>Capital turns</h3><strong>{exactRate(dashboard.personalLearning.capitalTurns, 'turns')}</strong><p>Time-weighted matched capital over the measured interval.</p></section>
      </div>
      <DashboardTable title="Personal market evidence" columns={['Item', 'Evidence strength', 'Known-basis outcomes', 'Average capital lock', 'Profit/day', 'Capital turns', 'Observed timing']}>
        {dashboard.personalLearning.items.length === 0 ? <tr><td colSpan={7}>No item-level personal evidence is retained yet.</td></tr> : dashboard.personalLearning.items.map((item) => <tr key={item.itemId}><td>{item.itemName}</td><td>{personalLearningStatus(item.status)}</td><td>{item.knownBasisSampleCount ?? 0}</td><td>{item.averageHoldingDuration ?? 'Unavailable'}</td><td>{exactRate(item.realizedProfitPerDay, 'c/day')}</td><td>{exactRate(item.capitalTurns, 'turns')}</td><td>{item.exactSourceTimestampCount} source / {item.intervalCensoredCompletionCount} bounded / {item.observedQuantityReductionCount} reductions / {item.unknownOrderTimingCount} unknown</td></tr>)}
      </DashboardTable>
    </section>}
    <DashboardTable title="Current orders" columns={['Side', 'Item', 'Quantity', 'Your price', 'Current market']}>
      {dashboard.currentOrders.length === 0 ? <tr><td colSpan={5}>No current orders in the latest sync.</td></tr> : dashboard.currentOrders.map((order) => <tr key={order.orderId}><td>{order.side}</td><td>{order.itemName}</td><td>{order.quantity}</td><td>{copper(order.unitPrice)}</td><td>{order.marketComparisonStatus === 'available' ? copper(order.currentMarketUnitPrice) : order.marketComparisonStatus === 'missingSide' ? 'No comparable orders' : 'Market unavailable'}</td></tr>)}
    </DashboardTable>
    <DashboardTable title="Recent completed trades" columns={['Side', 'Item', 'Quantity', 'Price', 'Completed']}>
      {dashboard.recentTrades.length === 0 ? <tr><td colSpan={5}>No completed trades are retained yet.</td></tr> : dashboard.recentTrades.map((trade) => <tr key={trade.transactionId}><td>{trade.side}</td><td>{trade.itemName}</td><td>{trade.quantity}</td><td>{copper(trade.unitPrice)}</td><td>{timestamp(trade.completedAtUtc)}</td></tr>)}
    </DashboardTable>
    <div className="dashboard-split"><DashboardItems title="Best realized items" items={dashboard.bestRealizedItems} /><DashboardItems title="Worst realized items" items={dashboard.worstRealizedItems} /></div>
    {dashboard.openInventory.length > 0 && <DashboardTable title="Open inventory" columns={['Item', 'Quantity', 'Basis', 'Liquidation state', 'Unrealized P&L']}>
      {dashboard.openInventory.map((item) => <tr key={item.itemId}><td>{item.itemName}</td><td>{item.quantity}</td><td>{copper(item.acquisitionBasis)}</td><td>{item.liquidationStatus === 'fullyValued' ? 'Fully valued' : item.liquidationStatus === 'insufficientBuyDepth' ? `Insufficient buy depth (${item.unliquidatedQuantity} remaining)` : 'Market evidence missing'}</td><td>{copper(item.unrealizedProfit)}</td></tr>)}
    </DashboardTable>}
  </section>;
}

function DashboardTable({ title, columns, children }: { title: string; columns: string[]; children: ReactNode }) {
  return <section className="dashboard-table"><h3>{title}</h3><div className="table-wrap"><table><thead><tr>{columns.map((column) => <th key={column} scope="col">{column}</th>)}</tr></thead><tbody>{children}</tbody></table></div></section>;
}

function DashboardItems({ title, items }: { title: string; items: Dashboard['bestRealizedItems'] }) {
  return <section className="dashboard-items"><h3>{title}</h3>{items.length === 0 ? <p>No known-basis realized sales yet.</p> : <ol>{items.map((item) => <li key={item.itemId}><span>{item.itemName} ({item.quantity})</span><strong>{copper(item.netProfit)}</strong></li>)}</ol>}</section>;
}

void DashboardPanel;

function LocalDataPanel({ onPersonalDataChanged }: { onPersonalDataChanged: () => void }) {
  const [location, setLocation] = useState<LocalDataLocationState>({ kind: 'loading' });
  const [locationRefreshGeneration, setLocationRefreshGeneration] = useState(0);
  const [message, setMessage] = useState<string | null>(null);
  const [isBackingUp, setIsBackingUp] = useState(false);
  const [restoreFile, setRestoreFile] = useState<File | null>(null);
  const restoreFileInput = useRef<HTMLInputElement>(null);
  const [managedBackupFileName, setManagedBackupFileName] = useState('');
  const [restoreConfirmation, setRestoreConfirmation] = useState('');
  const [clearConfirmation, setClearConfirmation] = useState('');
  const [isRestoring, setIsRestoring] = useState(false);
  const [isClearing, setIsClearing] = useState(false);
  const isRecoveryBusy = isBackingUp || isRestoring || isClearing;

  useEffect(() => {
    const controller = new AbortController();
    void fetch('/api/local-data', { headers: localRequestHeaders(), signal: controller.signal })
      .then(async (response) => {
        const payload: unknown = await response.json();
        if (response.ok && isLocalDataLocation(payload)) {
          setLocation({ kind: 'ready', location: payload });
        } else {
          setLocation({ kind: 'unavailable' });
        }
      })
      .catch((error) => {
        if (!(error instanceof DOMException && error.name === 'AbortError')) {
          setLocation({ kind: 'unavailable' });
        }
      });
    return () => controller.abort();
  }, [locationRefreshGeneration]);

  const createBackup = () => {
    setIsBackingUp(true);
    setMessage(null);
    void fetch('/api/local-data/backup', { method: 'POST', headers: localRequestHeaders() })
      .then(async (response) => {
        if (!response.ok) {
          setMessage('Backup could not be created. Your current local data has not been changed.');
          return;
        }
        const payload: unknown = await response.json();
        if (typeof payload !== 'object' || payload === null || typeof (payload as Record<string, unknown>).fileName !== 'string') {
          setMessage('Backup outcome could not be confirmed. Check the local backup folder before retrying.');
          return;
        }
        setMessage(`Backup created: ${(payload as Record<string, string>).fileName}`);
        setLocationRefreshGeneration((generation) => generation + 1);
      })
      .catch(() => setMessage('Backup outcome could not be confirmed. Check the local backup folder before retrying.'))
      .finally(() => setIsBackingUp(false));
  };

  const refreshManagedBackups = () => {
    setManagedBackupFileName('');
    setLocationRefreshGeneration((generation) => generation + 1);
  };

  const restore = () => {
    if (restoreFile === null || restoreConfirmation !== 'RESTORE LOCAL DATA') {
      return;
    }

    setIsRestoring(true);
    setMessage(null);
    const importedBackupUploadLimit = location.kind === 'ready'
      ? location.location.managedBackupUploadLimitBytes
      : undefined;
    if (importedBackupUploadLimit !== undefined && restoreFile.size > importedBackupUploadLimit) {
      setMessage('This imported backup exceeds the local upload limit. Only application-created Tyrian Ledger backups already moved into the managed Backups folder can be selected for local managed restore. Refresh the managed backup list after moving one.');
      setIsRestoring(false);
      return;
    }

    const form = new FormData();
    form.append('backup', restoreFile);
    form.append('confirmation', restoreConfirmation);
    void fetch('/api/local-data/restore', { method: 'POST', headers: localRequestHeaders(), body: form })
      .then(async (response) => {
        if (!response.ok) {
          setMessage('The selected backup could not be restored. Your current local data was kept.');
          return;
        }
        const payload: unknown = await response.json();
        if (typeof payload !== 'object' || payload === null || (payload as Record<string, unknown>).outcome !== 'restored') {
          setMessage('Restore outcome could not be confirmed. Check local data before retrying.');
          return;
        }
        const preRestore = (payload as Record<string, unknown>).preRestoreBackupFileName;
        setMessage(typeof preRestore === 'string'
          ? `Backup restored. Your previous data was saved as ${preRestore}.`
          : 'Backup restored.');
        setRestoreFile(null);
        setRestoreConfirmation('');
        if (restoreFileInput.current !== null) {
          restoreFileInput.current.value = '';
        }
        onPersonalDataChanged();
        setLocationRefreshGeneration((generation) => generation + 1);
      })
      .catch(() => setMessage('Restore outcome could not be confirmed. Check local data before retrying.'))
      .finally(() => setIsRestoring(false));
  };

  const restoreManagedBackup = () => {
    if (managedBackupFileName === '' || restoreConfirmation !== 'RESTORE LOCAL DATA') {
      return;
    }

    setIsRestoring(true);
    setMessage(null);
    void fetch('/api/local-data/restore-managed', {
      method: 'POST',
      headers: { ...localRequestHeaders(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ confirmation: restoreConfirmation, backupFileName: managedBackupFileName }),
    })
      .then(async (response) => {
        if (!response.ok) {
          setMessage('The selected managed backup could not be restored. Your current local data was kept.');
          return;
        }
        const payload: unknown = await response.json();
        if (typeof payload !== 'object' || payload === null || (payload as Record<string, unknown>).outcome !== 'restored') {
          setMessage('Restore outcome could not be confirmed. Check local data before retrying.');
          return;
        }
        const preRestore = (payload as Record<string, unknown>).preRestoreBackupFileName;
        setMessage(typeof preRestore === 'string'
          ? `Backup restored. Your previous data was saved as ${preRestore}.`
          : 'Backup restored.');
        setManagedBackupFileName('');
        setRestoreConfirmation('');
        onPersonalDataChanged();
        setLocationRefreshGeneration((generation) => generation + 1);
      })
      .catch(() => setMessage('Restore outcome could not be confirmed. Check local data before retrying.'))
      .finally(() => setIsRestoring(false));
  };

  const clearPersonalData = () => {
    if (clearConfirmation !== 'CLEAR PERSONAL DATA') {
      return;
    }

    setIsClearing(true);
    setMessage(null);
    void fetch('/api/local-data/clear-personal', {
      method: 'POST',
      headers: { ...localRequestHeaders(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ confirmation: clearConfirmation }),
    })
      .then(async (response) => {
        if (!response.ok) {
          setMessage('Personal data could not be cleared. Your current local data was kept.');
          return;
        }
        const payload: unknown = await response.json();
        if (typeof payload !== 'object' || payload === null || (payload as Record<string, unknown>).outcome !== 'personal_data_cleared') {
          setMessage('Clear outcome could not be confirmed. Check local data before retrying.');
          return;
        }
        setMessage('Personal account data cleared. Existing backup files were kept.');
        setClearConfirmation('');
        onPersonalDataChanged();
      })
      .catch(() => setMessage('Clear outcome could not be confirmed. Check local data before retrying.'))
      .finally(() => setIsClearing(false));
  };

  return (
    <section aria-labelledby="local-data-title" className="local-data-panel">
      <p className="eyebrow">Local data</p>
      <h2 id="local-data-title">Backup and recovery</h2>
      <p>Backups stay on this computer. Tyrian Ledger handles restore files only through its local loopback host and never sends them to a cloud service.</p>
      {location.kind === 'loading' && <p aria-live="polite" role="status">Finding local data locations…</p>}
      {location.kind === 'unavailable' && <p role="alert">Local data locations are unavailable. Check that the local host is running.</p>}
      {location.kind === 'ready' && (
        <dl className="local-data-locations">
          <div><dt>Database</dt><dd><code>{location.location.databasePath}</code></dd></div>
          <div><dt>Backups</dt><dd><code>{location.location.backupDirectoryPath}</code></dd></div>
        </dl>
      )}
      <div className="local-data-action">
        <h3>Create a backup</h3>
        <p>Create a timestamped, consistent copy before making major changes to your computer or this application.</p>
        <button disabled={isRecoveryBusy || location.kind !== 'ready'} onClick={createBackup} type="button">
          {isBackingUp ? 'Creating backup…' : 'Create local backup'}
        </button>
      </div>
      <div className="local-data-action">
        <h3>Restore a backup</h3>
        <p>Restoring replaces the active database only after the selected file is checked. A backup of the current data is created first.</p>
        {location.kind === 'ready' && location.location.managedBackupUploadLimitBytes !== undefined && <p>Imported selected backup files are limited to {Math.floor(location.location.managedBackupUploadLimitBytes / (1024 * 1024))} MiB.</p>}
        <label htmlFor="restore-backup">Backup file</label>
        <input ref={restoreFileInput} id="restore-backup" accept=".db,application/x-sqlite3" onChange={(event) => setRestoreFile(event.target.files?.[0] ?? null)} type="file" />
        <label htmlFor="restore-confirmation">Type RESTORE LOCAL DATA to continue</label>
        <input id="restore-confirmation" value={restoreConfirmation} onChange={(event) => setRestoreConfirmation(event.target.value)} />
        <button disabled={isRecoveryBusy || restoreFile === null || restoreConfirmation !== 'RESTORE LOCAL DATA'} onClick={restore} type="button">
          {isRestoring ? 'Restoring backup…' : 'Restore selected backup'}
        </button>
        {location.kind === 'ready' && location.location.managedBackups !== undefined && <>
          <p>Managed restore is only for application-created Tyrian Ledger backups listed in this application’s Backups folder. After moving one there, refresh this list before selecting it.</p>
          <button disabled={isRecoveryBusy} onClick={refreshManagedBackups} type="button">Refresh managed backups</button>
          <label htmlFor="managed-restore-backup">Managed backup</label>
          <select id="managed-restore-backup" value={managedBackupFileName} onChange={(event) => setManagedBackupFileName(event.target.value)}>
            <option value="">Select a managed backup</option>
            {location.location.managedBackups.map((backup) => <option key={backup.fileName} value={backup.fileName}>{backup.fileName}</option>)}
          </select>
          <button disabled={isRecoveryBusy || managedBackupFileName === '' || restoreConfirmation !== 'RESTORE LOCAL DATA'} onClick={restoreManagedBackup} type="button">
            {isRestoring ? 'Restoring backup…' : 'Restore managed backup'}
          </button>
        </>}
      </div>
      <div className="local-data-action local-data-action--danger">
        <h3>Clear personal account data</h3>
        <p>This permanently removes synced account history and current-order records from the active database. Shared item metadata and settings remain. Existing backup files are not deleted.</p>
        <label htmlFor="clear-confirmation">Type CLEAR PERSONAL DATA to continue</label>
        <input id="clear-confirmation" value={clearConfirmation} onChange={(event) => setClearConfirmation(event.target.value)} />
        <button disabled={isRecoveryBusy || clearConfirmation !== 'CLEAR PERSONAL DATA'} onClick={clearPersonalData} type="button">
          {isClearing ? 'Clearing personal data…' : 'Clear personal account data'}
        </button>
      </div>
      {message !== null && <p aria-live="polite" className="local-data-message" role="status">{message}</p>}
    </section>
  );
}
