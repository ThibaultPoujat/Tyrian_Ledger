import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createFixtureProvider, scenarioFromSearch } from '../experience/fixtureProvider';
import { SignalsExperience } from '../experience/SignalsExperience';

const rootElement = document.getElementById('p05a-preview-root');
if (rootElement === null) throw new Error('Preview root element is missing');

const scenario = scenarioFromSearch(window.location.search);
createRoot(rootElement).render(
  <StrictMode>
    <SignalsExperience provider={createFixtureProvider(scenario)} initialDestination={new URLSearchParams(window.location.search).get('view') === 'plans' || !['normal', 'urgent', 'degraded'].includes(scenario) ? 'plans' : 'signals'} />
  </StrictMode>,
);
