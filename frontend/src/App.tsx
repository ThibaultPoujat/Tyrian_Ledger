import './App.css';

type M9Route = 'recommendations' | 'settings';

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

export default function App() {
  const route = getRoute(window.location.pathname);

  if (route === null) {
    return (
      <main className="m9-shell" data-testid="unavailable-route">
        <p className="eyebrow">Tyrian Ledger</p>
        <h1>Route unavailable</h1>
        <p>This route is not part of the beginner fast-flip MVP.</p>
        <a href="/recommendations">Go to Recommendations</a>
      </main>
    );
  }

  const isRecommendations = route === 'recommendations';
  return (
    <main className="m9-shell">
      <header className="m9-header">
        <p className="eyebrow">Tyrian Ledger</p>
        <nav aria-label="Primary navigation">
          <a aria-current={isRecommendations ? 'page' : undefined} href="/recommendations">Recommendations</a>
          <a aria-current={isRecommendations ? undefined : 'page'} href="/settings">Settings</a>
        </nav>
      </header>

      <section aria-labelledby="m9-title" className="m9-placeholder">
        <h1 id="m9-title">{isRecommendations ? 'Recommendations' : 'Settings'}</h1>
        <p>
          {isRecommendations
            ? 'Your player-triggered market scan and fast-flip recommendations will appear here.'
            : 'Your available capital and risk choice will be configured here.'}
        </p>
      </section>
    </main>
  );
}
