import './App.css';

export default function App() {
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
            <p className="eyebrow">M12 transition</p>
            <h1 id="transition-title">The public trading assistant has been retired.</h1>
            <p className="page-introduction">
              Tyrian Ledger is moving to a local-first personal Trading Post assistant.
              This version no longer publishes market snapshots or calculates recommendations in the browser.
            </p>
            <div className="transition-details">
              <section aria-labelledby="preserved-title">
                <h2 id="preserved-title">What remains</h2>
                <p>Its deterministic C# finance, order-book, market-gateway, and accessibility foundations remain available for the local runtime.</p>
              </section>
              <section aria-labelledby="next-title">
                <h2 id="next-title">What comes next</h2>
                <p>Later milestones add the loopback host, locally owned data, and structured server-side results. No account data, API key, or browser storage is used here.</p>
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
