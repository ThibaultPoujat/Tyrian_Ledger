import { useEffect, useState } from 'react';
import './App.css';

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
};

type LocalDataLocationState =
  | { kind: 'loading' }
  | { kind: 'unavailable' }
  | { kind: 'ready'; location: LocalDataLocation };

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
      return 'Checking ArenaNet key status…';
    case 'not_configured':
      return 'No ArenaNet key configured';
    case 'valid':
      return 'Account connection ready';
    case 'invalid':
      return 'ArenaNet key is invalid or revoked';
    case 'insufficient_permissions':
      return `ArenaNet key needs permission${missingPermissions.length === 1 ? '' : 's'}: ${missingPermissions.join(', ')}`;
    case 'unavailable':
      return 'ArenaNet key status unavailable';
  }
}

function isLocalDataLocation(payload: unknown): payload is LocalDataLocation {
  if (typeof payload !== 'object' || payload === null) {
    return false;
  }

  const candidate = payload as Record<string, unknown>;
  return typeof candidate.databasePath === 'string' && typeof candidate.backupDirectoryPath === 'string';
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
    <div className="app-page">
      <a className="skip-link" href="#main-content">Skip to main content</a>
      <div className="app-shell">
        <header className="app-header">
          <div aria-label="Tyrian Ledger" className="brand-lockup">
            <span aria-hidden="true" className="brand-mark">TL</span>
            <span><strong>Tyrian Ledger</strong><small>Personal trading assistant</small></span>
          </div>
        </header>

        <main id="main-content">
          <section aria-labelledby="transition-title" className="transition-panel">
            <p className="eyebrow">M13 local runtime</p>
            <h1 id="transition-title">The local application foundation is running.</h1>
            <p className="page-introduction">
              Tyrian Ledger now pairs this React interface with a loopback-only local host.
              Account data, trading features, and recommendations are not part of this foundation yet.
            </p>
            <p aria-live="polite" className={`host-status host-status--${hostStatus}`} role="status">
              <span aria-hidden="true" />
              {hostStatus === 'checking' && 'Checking the local host…'}
              {hostStatus === 'connected' && 'Local host connected'}
              {hostStatus === 'unavailable' && 'Local host unavailable'}
            </p>
            <section aria-labelledby="account-connection-title" className="account-connection-panel">
              <p className="eyebrow">Account connection</p>
              <h2 id="account-connection-title">Keep your key on this computer</h2>
              <p aria-live="polite" className={`account-connection-status account-connection-status--${accountConnection.state}`} role="status">
                <span aria-hidden="true" />
                {accountConnectionMessage(accountConnection.state, accountConnection.missingPermissions)}
              </p>
              {(accountConnection.state === 'not_configured' || accountConnection.state === 'unavailable') && (
                <p>Store a dedicated read-only ArenaNet key in your operating system’s credential vault. Tyrian Ledger never asks the browser to store or send it.</p>
              )}
              {accountConnection.state === 'insufficient_permissions' && (
                <p>Use a dedicated key with account and trading-post access for future personal Trading Post features.</p>
              )}
            </section>
            <LocalDataPanel />
            <div className="transition-details">
              <section aria-labelledby="runtime-title">
                <h2 id="runtime-title">Local by default</h2>
                <p>The host listens only on this computer and serves the built interface and API from the same origin for normal use.</p>
              </section>
              <section aria-labelledby="boundary-title">
                <h2 id="boundary-title">A safe starting point</h2>
                <p>No ArenaNet key is required. Local backups and recovery stay on this computer; trading, scanner, and recommendation features are not automated.</p>
              </section>
            </div>
          </section>
        </main>
      </div>
      <footer className="site-footer">
        <div><strong>Tyrian Ledger</strong><span>Local-first, read-only Trading Post decision support.</span></div>
        <p>Tyrian Ledger is an unofficial, independent Guild Wars 2 fan project and is not affiliated with or endorsed by ArenaNet or NCSOFT.</p>
        <p>Guild Wars 2 © ArenaNet, LLC. All rights reserved. Guild Wars 2 and GW2 are trademarks or registered trademarks of NCSOFT Corporation.</p>
      </footer>
    </div>
  );
}

function LocalDataPanel() {
  const [location, setLocation] = useState<LocalDataLocationState>({ kind: 'loading' });
  const [message, setMessage] = useState<string | null>(null);
  const [isBackingUp, setIsBackingUp] = useState(false);
  const [restoreFile, setRestoreFile] = useState<File | null>(null);
  const [restoreConfirmation, setRestoreConfirmation] = useState('');
  const [clearConfirmation, setClearConfirmation] = useState('');
  const [isRestoring, setIsRestoring] = useState(false);
  const [isClearing, setIsClearing] = useState(false);

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
  }, []);

  const createBackup = () => {
    setIsBackingUp(true);
    setMessage(null);
    void fetch('/api/local-data/backup', { method: 'POST', headers: localRequestHeaders() })
      .then(async (response) => {
        const payload: unknown = await response.json();
        if (!response.ok || typeof payload !== 'object' || payload === null || typeof (payload as Record<string, unknown>).fileName !== 'string') {
          throw new Error('backup failed');
        }
        setMessage(`Backup created: ${(payload as Record<string, string>).fileName}`);
      })
      .catch(() => setMessage('Backup could not be created. Your current local data has not been changed.'))
      .finally(() => setIsBackingUp(false));
  };

  const restore = () => {
    if (restoreFile === null || restoreConfirmation !== 'RESTORE LOCAL DATA') {
      return;
    }

    setIsRestoring(true);
    setMessage(null);
    const form = new FormData();
    form.append('backup', restoreFile);
    form.append('confirmation', restoreConfirmation);
    void fetch('/api/local-data/restore', { method: 'POST', headers: localRequestHeaders(), body: form })
      .then(async (response) => {
        const payload: unknown = await response.json();
        if (!response.ok || typeof payload !== 'object' || payload === null || (payload as Record<string, unknown>).outcome !== 'restored') {
          throw new Error('restore failed');
        }
        const preRestore = (payload as Record<string, unknown>).preRestoreBackupFileName;
        setMessage(typeof preRestore === 'string'
          ? `Backup restored. Your previous data was saved as ${preRestore}.`
          : 'Backup restored.');
        setRestoreFile(null);
        setRestoreConfirmation('');
      })
      .catch(() => setMessage('The selected backup could not be restored. Your current local data was kept.'))
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
        const payload: unknown = await response.json();
        if (!response.ok || typeof payload !== 'object' || payload === null || (payload as Record<string, unknown>).outcome !== 'personal_data_cleared') {
          throw new Error('clear failed');
        }
        setMessage('Personal account data cleared. Existing backup files were kept.');
        setClearConfirmation('');
      })
      .catch(() => setMessage('Personal data could not be cleared. Your current local data was kept.'))
      .finally(() => setIsClearing(false));
  };

  return (
    <section aria-labelledby="local-data-title" className="local-data-panel">
      <p className="eyebrow">Local data</p>
      <h2 id="local-data-title">Backup and recovery</h2>
      <p>Backups stay on this computer. Tyrian Ledger never uploads your database or your backup files.</p>
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
        <button disabled={isBackingUp || location.kind !== 'ready'} onClick={createBackup} type="button">
          {isBackingUp ? 'Creating backup…' : 'Create local backup'}
        </button>
      </div>
      <div className="local-data-action">
        <h3>Restore a backup</h3>
        <p>Restoring replaces the active database only after the selected file is checked. A backup of the current data is created first.</p>
        <label htmlFor="restore-backup">Backup file</label>
        <input id="restore-backup" accept=".db,application/x-sqlite3" onChange={(event) => setRestoreFile(event.target.files?.[0] ?? null)} type="file" />
        <label htmlFor="restore-confirmation">Type RESTORE LOCAL DATA to continue</label>
        <input id="restore-confirmation" value={restoreConfirmation} onChange={(event) => setRestoreConfirmation(event.target.value)} />
        <button disabled={isRestoring || restoreFile === null || restoreConfirmation !== 'RESTORE LOCAL DATA'} onClick={restore} type="button">
          {isRestoring ? 'Restoring backup…' : 'Restore selected backup'}
        </button>
      </div>
      <div className="local-data-action local-data-action--danger">
        <h3>Clear personal account data</h3>
        <p>This permanently removes synced account history and current-order records from the active database. Shared item metadata and settings remain. Existing backup files are not deleted.</p>
        <label htmlFor="clear-confirmation">Type CLEAR PERSONAL DATA to continue</label>
        <input id="clear-confirmation" value={clearConfirmation} onChange={(event) => setClearConfirmation(event.target.value)} />
        <button disabled={isClearing || clearConfirmation !== 'CLEAR PERSONAL DATA'} onClick={clearPersonalData} type="button">
          {isClearing ? 'Clearing personal data…' : 'Clear personal account data'}
        </button>
      </div>
      {message !== null && <p aria-live="polite" className="local-data-message" role="status">{message}</p>}
    </section>
  );
}
