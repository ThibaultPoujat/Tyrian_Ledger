import { useEffect, useRef, useState } from 'react';
import './App.css';
import RecommendationPanel from './RecommendationPanel';
import PlanPanel from './PlanPanel';
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
const restoreConfirmationText = 'RESTAURER LES DONNÉES LOCALES';
const clearConfirmationText = 'EFFACER LES DONNÉES PERSONNELLES';

type Dashboard = {
  state: 'ready' | 'notSynchronized' | 'accountUnavailable';
  historyCoverage: { startUtc: string | null; endUtc: string | null } | null;
  realizedWindows: Array<{ days: number; status: 'supported' | 'insufficientCoverage'; netProfit: Money | null; unknownBasisQuantity: number }>;
  todayRealized: { days: number; status: 'supported' | 'insufficientCoverage'; netProfit: Money | null; unknownBasisQuantity: number } | null;
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

function syncFailureMessage(error: string | null): string {
  const messages: Record<string, string> = {
    credential_not_configured: "Aucune clé ArenaNet n'est configurée. Vérifiez la connexion du compte dans les réglages.",
    credential_unavailable: "La clé ArenaNet enregistrée n'est pas accessible. Vérifiez le coffre d'identifiants du système.",
    unauthorized: "La clé ArenaNet a été refusée. Vérifiez qu'elle est toujours valide.",
    forbidden: "La clé ArenaNet ne dispose pas des autorisations nécessaires : account, tradingpost et wallet.",
    rate_limited: "ArenaNet limite temporairement les requêtes. Réessayez dans quelques instants.",
    upstream_unavailable: "ArenaNet est temporairement indisponible. Vos données locales existantes sont conservées.",
    transport_failure: "La requête vers ArenaNet a échoué ou a expiré. Réessayez dans quelques instants.",
    persistence_failure: "La synchronisation a été reçue, mais l'enregistrement local a échoué. Vos données locales existantes sont conservées.",
    invalid_payload: "ArenaNet a renvoyé des données inattendues. Vos données locales existantes sont conservées.",
    incomplete_data: "Les données reçues d'ArenaNet sont incomplètes. Vos données locales existantes sont conservées.",
    invalid_request: "La requête de synchronisation a été refusée comme invalide. Vos données locales existantes sont conservées.",
    not_found: "Une ressource ArenaNet nécessaire à la synchronisation est introuvable.",
    unexpected_response: "ArenaNet a renvoyé une réponse inattendue. Vos données locales existantes sont conservées.",
  };
  return error !== null && messages[error] !== undefined
    ? messages[error]
    : "La synchronisation n'a pas pu être confirmée. Les données locales existantes sont conservées.";
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
  const [syncStatus, setSyncStatus] = useState<string>('idle');
  const [localDataRefreshGeneration, setLocalDataRefreshGeneration] = useState(0);
  const [activeView, setActiveView] = useState<'signals' | 'plans' | 'settings'>('signals');
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
    setDashboard(null);
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
          const error = typeof payload === 'object' && payload !== null && typeof (payload as Record<string, unknown>).error === 'string'
            ? (payload as Record<string, string>).error
            : null;
          setSyncStatus(error === null ? 'failed' : `failed:${error}`);
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
            aria-label="Mes Signaux"
            aria-current={activeView === 'signals' ? 'page' : undefined}
            className={activeView === 'signals' ? 'nav-item nav-item--active' : 'nav-item'}
            onClick={() => setActiveView('signals')}
            type="button"
          >
            <span aria-hidden="true">◆</span>
            <span>Mes Signaux</span>
          </button>
          <button
            aria-label="Plans"
            aria-current={activeView === 'plans' ? 'page' : undefined}
            className={activeView === 'plans' ? 'nav-item nav-item--active' : 'nav-item'}
            onClick={() => setActiveView('plans')}
            type="button"
          >
            <span aria-hidden="true">◇</span>
            <span>Plans</span>
          </button>
          <button aria-label="Artisanat — bientôt" className="nav-item" disabled type="button">
            <span aria-hidden="true">⌁</span>
            <span>Artisanat <small>Bientôt</small></span>
          </button>
          <button
            aria-label="Réglages"
            aria-current={activeView === 'settings' ? 'page' : undefined}
            className={activeView === 'settings' ? 'nav-item nav-item--active' : 'nav-item'}
            onClick={() => setActiveView('settings')}
            type="button"
          >
            <span aria-hidden="true">⚙</span>
            <span>Réglages</span>
          </button>
        </nav>
        <div aria-live="polite" className="sidebar-status" role="status">
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
        ) : activeView === 'plans' ? (
          <>
            <header className="signals-header"><div><p className="eyebrow">Exécution guidée</p><h1>Plans</h1><p className="page-introduction">Une action utile à la fois, avec une réconciliation explicite.</p></div></header>
            <PlanPanel />
          </>
        ) : (
          <section aria-labelledby="settings-title" className="settings-view">
            <header className="settings-header">
              <h1 id="settings-title">Réglages</h1>
              <p>Compte ArenaNet, données locales et diagnostic.</p>
            </header>

            <section aria-labelledby="account-connection-title" className="settings-panel settings-panel--primary">
              <h2 id="account-connection-title">Compte ArenaNet</h2>
              <p className="settings-section-note">Connexion en lecture seule</p>
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
              {syncStatus.startsWith('failed') && <p role="alert">{syncFailureMessage(syncStatus.includes(':') ? syncStatus.slice(syncStatus.indexOf(':') + 1) : null)}</p>}
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
  const excluded = (window: { unknownBasisQuantity: number } | null) =>
    window !== null && window.unknownBasisQuantity > 0
      ? <small>{window.unknownBasisQuantity} unité{window.unknownBasisQuantity === 1 ? '' : 's'} vendue{window.unknownBasisQuantity === 1 ? '' : 's'} exclue{window.unknownBasisQuantity === 1 ? '' : 's'} (prix d'achat inconnu)</small>
      : null;
  const unavailable = status === 'loading'
    ? 'Chargement…'
    : status === 'error' || dashboard === null
      ? 'Indisponible'
      : dashboard.state === 'notSynchronized'
        ? 'Non synchronisé'
        : dashboard.state === 'accountUnavailable'
          ? 'Compte indisponible'
          : 'Couverture insuffisante';
  const todayThrough = value(today) && dashboard?.historyCoverage?.endUtc
    ? new Date(dashboard.historyCoverage.endUtc).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' })
    : null;

  return (
    <section aria-label="Profit réalisé" className="performance-summary">
      <span className="performance-title">Profit réalisé</span>
      <div className="performance-primary">
        <div><span>Aujourd'hui</span>{value(today) ? <MoneyDisplay compact money={value(today)} /> : <strong>{unavailable}</strong>}{todayThrough && <small>Données jusqu’au {todayThrough}</small>}{excluded(today)}</div>
        <div><span>30 j</span>{value(thirty) ? <MoneyDisplay compact money={value(thirty)} /> : <strong>{unavailable}</strong>}{excluded(thirty)}</div>
      </div>
      <details>
        <summary>7 j / 90 j</summary>
        <div className="performance-secondary">
          <div><span>7 jours</span>{value(seven) ? <MoneyDisplay compact money={value(seven)} /> : <strong>{unavailable}</strong>}{excluded(seven)}</div>
          <div><span>90 jours</span>{value(ninety) ? <MoneyDisplay compact money={value(ninety)} /> : <strong>{unavailable}</strong>}{excluded(ninety)}</div>
        </div>
      </details>
    </section>
  );
}

function isDashboard(payload: unknown): payload is Dashboard {
  if (!isRecord(payload)) return false;
  return isOneOf(payload.state, ['ready', 'notSynchronized', 'accountUnavailable'])
    && isNullableHistoryCoverage(payload.historyCoverage)
    && isArrayOf(payload.realizedWindows, isRealizedWindow)
    && (payload.todayRealized === null || isRealizedWindow(payload.todayRealized));
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
  const [diagnosticMessage, setDiagnosticMessage] = useState<string | null>(null);
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
          setMessage("La sauvegarde n'a pas pu être créée. Vos données locales actuelles n'ont pas été modifiées.");
          return;
        }
        const payload: unknown = await response.json();
        if (typeof payload !== 'object' || payload === null || typeof (payload as Record<string, unknown>).fileName !== 'string') {
          setMessage("Le résultat de la sauvegarde n'a pas pu être confirmé. Vérifiez le dossier de sauvegardes locales avant de réessayer.");
          return;
        }
        setMessage(`Sauvegarde créée : ${(payload as Record<string, string>).fileName}`);
        setLocationRefreshGeneration((generation) => generation + 1);
      })
      .catch(() => setMessage("Le résultat de la sauvegarde n'a pas pu être confirmé. Vérifiez le dossier de sauvegardes locales avant de réessayer."))
      .finally(() => setIsBackingUp(false));
  };

  const refreshManagedBackups = () => {
    setManagedBackupFileName('');
    setLocationRefreshGeneration((generation) => generation + 1);
  };

  const restore = () => {
    if (restoreFile === null || restoreConfirmation !== restoreConfirmationText) {
      return;
    }

    setIsRestoring(true);
    setMessage(null);
    const importedBackupUploadLimit = location.kind === 'ready'
      ? location.location.managedBackupUploadLimitBytes
      : undefined;
    if (importedBackupUploadLimit !== undefined && restoreFile.size > importedBackupUploadLimit) {
      setMessage('Cette sauvegarde importée dépasse la limite locale. Déplacez une sauvegarde créée par Tyrian Ledger dans le dossier Backups géré, puis actualisez la liste avant de la restaurer.');
      setIsRestoring(false);
      return;
    }

    const form = new FormData();
    form.append('backup', restoreFile);
    form.append('confirmation', 'RESTORE LOCAL DATA');
    void fetch('/api/local-data/restore', { method: 'POST', headers: localRequestHeaders(), body: form })
      .then(async (response) => {
        if (!response.ok) {
          setMessage("La sauvegarde sélectionnée n'a pas pu être restaurée. Vos données locales actuelles ont été conservées.");
          return;
        }
        const payload: unknown = await response.json();
        if (typeof payload !== 'object' || payload === null || (payload as Record<string, unknown>).outcome !== 'restored') {
          setMessage("Le résultat de la restauration n'a pas pu être confirmé. Vérifiez les données locales avant de réessayer.");
          return;
        }
        const preRestore = (payload as Record<string, unknown>).preRestoreBackupFileName;
        setMessage(typeof preRestore === 'string'
          ? `Sauvegarde restaurée. Vos données précédentes ont été enregistrées sous ${preRestore}.`
          : 'Sauvegarde restaurée.');
        setRestoreFile(null);
        setRestoreConfirmation('');
        if (restoreFileInput.current !== null) {
          restoreFileInput.current.value = '';
        }
        onPersonalDataChanged();
        setLocationRefreshGeneration((generation) => generation + 1);
      })
      .catch(() => setMessage("Le résultat de la restauration n'a pas pu être confirmé. Vérifiez les données locales avant de réessayer."))
      .finally(() => setIsRestoring(false));
  };

  const restoreManagedBackup = () => {
    if (managedBackupFileName === '' || restoreConfirmation !== restoreConfirmationText) {
      return;
    }

    setIsRestoring(true);
    setMessage(null);
    void fetch('/api/local-data/restore-managed', {
      method: 'POST',
      headers: { ...localRequestHeaders(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ confirmation: 'RESTORE LOCAL DATA', backupFileName: managedBackupFileName }),
    })
      .then(async (response) => {
        if (!response.ok) {
          setMessage("La sauvegarde gérée sélectionnée n'a pas pu être restaurée. Vos données locales actuelles ont été conservées.");
          return;
        }
        const payload: unknown = await response.json();
        if (typeof payload !== 'object' || payload === null || (payload as Record<string, unknown>).outcome !== 'restored') {
          setMessage("Le résultat de la restauration n'a pas pu être confirmé. Vérifiez les données locales avant de réessayer.");
          return;
        }
        const preRestore = (payload as Record<string, unknown>).preRestoreBackupFileName;
        setMessage(typeof preRestore === 'string'
          ? `Sauvegarde restaurée. Vos données précédentes ont été enregistrées sous ${preRestore}.`
          : 'Sauvegarde restaurée.');
        setManagedBackupFileName('');
        setRestoreConfirmation('');
        onPersonalDataChanged();
        setLocationRefreshGeneration((generation) => generation + 1);
      })
      .catch(() => setMessage("Le résultat de la restauration n'a pas pu être confirmé. Vérifiez les données locales avant de réessayer."))
      .finally(() => setIsRestoring(false));
  };

  const clearPersonalData = () => {
    if (clearConfirmation !== clearConfirmationText) {
      return;
    }

    setIsClearing(true);
    setMessage(null);
    void fetch('/api/local-data/clear-personal', {
      method: 'POST',
      headers: { ...localRequestHeaders(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ confirmation: 'CLEAR PERSONAL DATA' }),
    })
      .then(async (response) => {
        if (!response.ok) {
          setMessage("Les données personnelles n'ont pas pu être effacées. Vos données locales actuelles ont été conservées.");
          return;
        }
        const payload: unknown = await response.json();
        if (typeof payload !== 'object' || payload === null || (payload as Record<string, unknown>).outcome !== 'personal_data_cleared') {
          setMessage("Le résultat de l'effacement n'a pas pu être confirmé. Vérifiez les données locales avant de réessayer.");
          return;
        }
        setMessage('Données personnelles du compte effacées. Les sauvegardes existantes ont été conservées.');
        setClearConfirmation('');
        onPersonalDataChanged();
      })
      .catch(() => setMessage("Le résultat de l'effacement n'a pas pu être confirmé. Vérifiez les données locales avant de réessayer."))
      .finally(() => setIsClearing(false));
  };

  const exportDiagnostics = () => {
    setDiagnosticMessage(null);
    void fetch('/api/diagnostics/export', { headers: localRequestHeaders() })
      .then(async response => {
        if (!response.ok) throw new Error('Diagnostic export failed');
        const blob = await response.blob();
        const url = URL.createObjectURL(blob);
        const revokeObjectURL = URL.revokeObjectURL.bind(URL);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'tyrian-ledger-diagnostic.txt';
        link.click();
        window.setTimeout(() => revokeObjectURL(url), 0);
      })
      .catch(() => setDiagnosticMessage("Le diagnostic n'a pas pu être exporté."));
  };

  return (
    <>
      <section aria-labelledby="local-data-title" className="settings-panel">
        <h2 id="local-data-title">Données locales</h2>
        <p>Créez une sauvegarde avant une modification importante. Les sauvegardes restent sur cet ordinateur et ne sont jamais envoyées vers un service cloud.</p>
        {location.kind === 'loading' && <p aria-live="polite" role="status">Recherche des emplacements de données locales…</p>}
        {location.kind === 'unavailable' && <p role="alert">Les emplacements de données locales sont indisponibles. Vérifiez que l'application locale fonctionne.</p>}
        {location.kind === 'ready' && (
          <details className="settings-disclosure settings-disclosure--quiet">
            <summary>Emplacements locaux</summary>
            <dl className="local-data-locations">
              <div><dt>Base de données</dt><dd><code>{location.location.databasePath}</code></dd></div>
              <div><dt>Sauvegardes</dt><dd><code>{location.location.backupDirectoryPath}</code></dd></div>
            </dl>
          </details>
        )}

        <div className="settings-primary-action">
          <div>
            <h3>Sauvegarde</h3>
            <p>Conservez une copie cohérente et horodatée de vos données locales.</p>
          </div>
          <button disabled={isRecoveryBusy || location.kind !== 'ready'} onClick={createBackup} type="button">
            {isBackingUp ? 'Création de la sauvegarde…' : 'Créer une sauvegarde locale'}
          </button>
        </div>

        <details className="settings-disclosure">
          <summary>Restaurer des données</summary>
          <div className="settings-disclosure-content">
            <p>La restauration remplace la base active uniquement après vérification du fichier sélectionné. Une sauvegarde des données actuelles est créée auparavant.</p>
            {location.kind === 'ready' && location.location.managedBackupUploadLimitBytes !== undefined && <p>Les sauvegardes importées sélectionnées sont limitées à {Math.floor(location.location.managedBackupUploadLimitBytes / (1024 * 1024))} MiB.</p>}
            <label htmlFor="restore-backup">Fichier de sauvegarde</label>
            <input ref={restoreFileInput} id="restore-backup" accept=".db,application/x-sqlite3" onChange={(event) => setRestoreFile(event.target.files?.[0] ?? null)} type="file" />
            <label htmlFor="restore-confirmation">Saisissez {restoreConfirmationText} pour continuer</label>
            <input id="restore-confirmation" value={restoreConfirmation} onChange={(event) => setRestoreConfirmation(event.target.value)} />
            <button disabled={isRecoveryBusy || restoreFile === null || restoreConfirmation !== restoreConfirmationText} onClick={restore} type="button">
              {isRestoring ? 'Restauration en cours…' : 'Restaurer la sauvegarde sélectionnée'}
            </button>
            {location.kind === 'ready' && location.location.managedBackups !== undefined && <>
              <div className="settings-subsection">
                <h3>Sauvegarde gérée</h3>
                <p>Utilisez une sauvegarde créée par Tyrian Ledger présente dans le dossier Backups de l'application.</p>
                <button disabled={isRecoveryBusy} onClick={refreshManagedBackups} type="button">Actualiser les sauvegardes gérées</button>
                <label htmlFor="managed-restore-backup">Sauvegarde gérée</label>
                <select id="managed-restore-backup" value={managedBackupFileName} onChange={(event) => setManagedBackupFileName(event.target.value)}>
                  <option value="">Sélectionner une sauvegarde gérée</option>
                  {location.location.managedBackups.map((backup) => <option key={backup.fileName} value={backup.fileName}>{backup.fileName}</option>)}
                </select>
                <button disabled={isRecoveryBusy || managedBackupFileName === '' || restoreConfirmation !== restoreConfirmationText} onClick={restoreManagedBackup} type="button">
                  {isRestoring ? 'Restauration en cours…' : 'Restaurer la sauvegarde gérée'}
                </button>
              </div>
            </>}
          </div>
        </details>
        {message !== null && <p aria-live="polite" className="local-data-message" role="status">{message}</p>}
      </section>

      <section aria-labelledby="diagnostics-title" className="settings-panel settings-panel--support">
        <h2 id="diagnostics-title">Diagnostic</h2>
        <p>Exportez les événements techniques récents lorsqu'un problème doit être analysé. Les clés API et les en-têtes d'autorisation ne sont pas enregistrés.</p>
        <button className="settings-secondary-button" onClick={exportDiagnostics} type="button">
          Exporter le diagnostic
        </button>
        {diagnosticMessage && <p role="alert">{diagnosticMessage}</p>}
      </section>

      <details className="settings-panel settings-danger">
        <summary>Zone sensible</summary>
        <div className="settings-disclosure-content">
          <h2>Effacer les données personnelles locales</h2>
          <p>Cette action supprime définitivement de la base active l'historique synchronisé du compte et les ordres actuels. Les métadonnées partagées, les réglages et les sauvegardes existantes sont conservés.</p>
          <label htmlFor="clear-confirmation">Saisissez {clearConfirmationText} pour continuer</label>
          <input id="clear-confirmation" value={clearConfirmation} onChange={(event) => setClearConfirmation(event.target.value)} />
          <button disabled={isRecoveryBusy || clearConfirmation !== clearConfirmationText} onClick={clearPersonalData} type="button">
            {isClearing ? 'Effacement en cours…' : 'Effacer les données personnelles'}
          </button>
        </div>
      </details>
    </>
  );
}
