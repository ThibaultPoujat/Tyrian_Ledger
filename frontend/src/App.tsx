import { useEffect, useState } from 'react';
import './App.css';

type HostStatus = 'checking' | 'connected' | 'unavailable';

export default function App() {
  const [hostStatus, setHostStatus] = useState<HostStatus>('checking');

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
            <div className="transition-details">
              <section aria-labelledby="runtime-title">
                <h2 id="runtime-title">Local by default</h2>
                <p>The host listens only on this computer and serves the built interface and API from the same origin for normal use.</p>
              </section>
              <section aria-labelledby="boundary-title">
                <h2 id="boundary-title">A safe starting point</h2>
                <p>No ArenaNet key is required. No account, order, scanner, recommendation, or database feature has been added.</p>
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
