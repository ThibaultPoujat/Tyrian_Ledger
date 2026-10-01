import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import PlanPanel from './PlanPanel';
import { invalidateViewCache } from './viewQueryCache';

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it('retries the original step after it advances, while a deliberate action on the next step gets a new command', async () => {
  let commandSequence = 0;
  vi.stubGlobal('crypto', { randomUUID: () => `command-${++commandSequence}` });
  const planReads = [plansFor('step-a', '1'), plansFor('step-b', '2'), noPlans(), noPlans()];
  const posts: Array<{ body: Record<string, unknown>; headers: Headers }> = [];
  const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url === '/api/plans/context') return Promise.resolve(jsonResponse({ state: 'ready', accountCacheScope: 'scope-a' }));
    if (url === '/api/plans') return Promise.resolve(jsonResponse(planReads.shift() ?? noPlans()));
    if (url.endsWith('/complete')) {
      const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
      posts.push({ body, headers: new Headers(init?.headers) });
      if (posts.length === 1) return Promise.reject(new Error('response lost'));
      return Promise.resolve(jsonResponse({ state: 'reported', acknowledgement: { status: 'applied' } }));
    }
    throw new Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal('fetch', fetchMock);

  render(<PlanPanel />);
  await screen.findByRole('button', { name: 'Terminé' });
  fireEvent.click(screen.getByRole('button', { name: 'Terminé' }));
  await waitFor(() => expect(posts).toHaveLength(1));

  // A background read can advance the card without replaying the unknown command.
  invalidateViewCache(['plans']);
  await waitFor(() => expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/plans')).toHaveLength(2));
  await waitFor(() => expect(screen.getByText(/Objet B/)).toBeInTheDocument());
  expect(posts).toHaveLength(1);

  // This is a new user action on B. It is not interpreted as the retry of A.
  fireEvent.click(screen.getByRole('button', { name: 'Terminé' }));
  await waitFor(() => expect(posts).toHaveLength(2));
  await waitFor(() => expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/plans')).toHaveLength(3));
  fireEvent.click(screen.getByRole('button', { name: 'Actualiser' }));
  await waitFor(() => expect(posts).toHaveLength(3));
  await waitFor(() => expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/plans/context')).toHaveLength(1));

  const [firstA, actionB, retryA] = posts;
  expect(firstA.body).toMatchObject({ stepId: 'step-a', expectedRevision: '1', commandId: 'command-1', operation: 'ReportPerformed', quantity: 1, unitPriceCopper: '100' });
  expect(actionB.body).toMatchObject({ stepId: 'step-b', expectedRevision: '2', commandId: 'command-2' });
  expect(retryA.body).toEqual(firstA.body);
  for (const post of posts) expect(post.headers.get('X-Tyrian-Ledger-Account-View-Scope')).toBe('scope-a');
});

it('does not post while fresh scope is unavailable and retains the command for a later Actualiser', async () => {
  let commandSequence = 0;
  vi.stubGlobal('crypto', { randomUUID: () => `command-${++commandSequence}` });
  const contexts = [
    jsonResponse({ state: 'unavailable', accountCacheScope: null }, 503),
    jsonResponse({ state: 'ready', accountCacheScope: 'scope-a' }),
  ];
  let completionAttempts = 0;
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = String(input);
    if (url === '/api/plans/context') return Promise.resolve(contexts.shift()!);
    if (url === '/api/plans') return Promise.resolve(jsonResponse(plansFor('step-a', '1')));
    if (url.endsWith('/complete')) {
      completionAttempts += 1;
      if (completionAttempts === 1) return Promise.reject(new Error('response lost'));
      return Promise.resolve(jsonResponse({ state: 'already_applied', acknowledgement: { status: 'already_applied' } }));
    }
    throw new Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal('fetch', fetchMock);

  render(<PlanPanel />);
  fireEvent.click(await screen.findByRole('button', { name: 'Terminé' }));
  await waitFor(() => expect(completionAttempts).toBe(1));
  fireEvent.click(screen.getByRole('button', { name: 'Actualiser' }));
  await waitFor(() => expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/plans/context')).toHaveLength(1));
  await waitFor(() => expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/plans')).toHaveLength(2));
  expect(completionAttempts).toBe(1);

  fireEvent.click(screen.getByRole('button', { name: 'Actualiser' }));
  await waitFor(() => expect(completionAttempts).toBe(2));
  expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/plans/context')).toHaveLength(2);
});

it('discards an unknown command when a fresh preflight detects an account switch', async () => {
  let completionAttempts = 0;
  let planReads = 0;
  const fetchMock = vi.fn((input: RequestInfo | URL) => {
    const url = String(input);
    if (url === '/api/plans/context') return Promise.resolve(jsonResponse({ state: 'ready', accountCacheScope: 'scope-b' }));
    if (url === '/api/plans') {
      planReads += 1;
      return Promise.resolve(jsonResponse(planReads === 1 ? plansFor('step-a', '1', 'scope-a') : plansFor('step-a', '1', 'scope-b')));
    }
    if (url.endsWith('/complete')) {
      completionAttempts += 1;
      return Promise.reject(new Error('response lost'));
    }
    throw new Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal('fetch', fetchMock);

  render(<PlanPanel />);
  fireEvent.click(await screen.findByRole('button', { name: 'Terminé' }));
  await waitFor(() => expect(completionAttempts).toBe(1));
  fireEvent.click(screen.getByRole('button', { name: 'Actualiser' }));
  await waitFor(() => expect(planReads).toBe(2));
  expect(completionAttempts).toBe(1);

  fireEvent.click(screen.getByRole('button', { name: 'Actualiser' }));
  await waitFor(() => expect(planReads).toBe(3));
  expect(completionAttempts).toBe(1);
  expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/plans/context')).toHaveLength(1);
});

it('coalesces concurrent recovery clicks and retries several unknown commands once in creation order', async () => {
  let commandSequence = 0;
  vi.stubGlobal('crypto', { randomUUID: () => `command-${++commandSequence}` });
  const planReads = [plansFor('step-a', '1'), plansFor('step-b', '2'), plansFor('step-b', '2'), plansFor('step-b', '2')];
  let releaseContext!: (response: Response) => void;
  const waitingContext = new Promise<Response>(resolve => { releaseContext = resolve; });
  let contextReads = 0;
  const posts: Array<Record<string, unknown>> = [];
  const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url === '/api/plans/context') { contextReads += 1; return waitingContext; }
    if (url === '/api/plans') return Promise.resolve(jsonResponse(planReads.shift() ?? noPlans()));
    if (url.endsWith('/complete')) {
      const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
      posts.push(body);
      if (posts.length <= 2) return Promise.reject(new Error('response lost'));
      return Promise.resolve(jsonResponse({ error: 'plan_command_conflict' }, 409));
    }
    throw new Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal('fetch', fetchMock);

  render(<PlanPanel />);
  fireEvent.click(await screen.findByRole('button', { name: 'Terminé' }));
  await waitFor(() => expect(posts).toHaveLength(1));
  invalidateViewCache(['plans']);
  await waitFor(() => expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/plans')).toHaveLength(2));
  await waitFor(() => expect(screen.getByText(/Objet B/)).toBeInTheDocument());
  fireEvent.click(screen.getByRole('button', { name: 'Terminé' }));
  await waitFor(() => expect(posts).toHaveLength(2));

  fireEvent.click(screen.getByRole('button', { name: 'Actualiser' }));
  await waitFor(() => expect(contextReads).toBe(1));
  fireEvent.click(screen.getByRole('button', { name: 'Actualisation en cours…' }));
  expect(contextReads).toBe(1);
  releaseContext(jsonResponse({ state: 'ready', accountCacheScope: 'scope-a' }));

  await waitFor(() => expect(posts).toHaveLength(4));
  await waitFor(() => expect(fetchMock.mock.calls.filter(([url]) => String(url) === '/api/plans')).toHaveLength(3));
  expect(posts.map(body => [body.stepId, body.commandId, body.expectedRevision])).toEqual([
    ['step-a', 'command-1', '1'],
    ['step-b', 'command-2', '2'],
    ['step-a', 'command-1', '1'],
    ['step-b', 'command-2', '2'],
  ]);
  expect(contextReads).toBe(1);
});

it('explains when cached Signals cannot become executable Plans because sizing is unavailable', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse({
    state: 'ready',
    proposals: [],
    plans: [],
    selection: {
      recommendationCandidates: 17,
      generatedCandidates: 17,
      rejectedHardConstraints: 0,
      rejectedResourceConflicts: 0,
      rejectedSelectionConstraints: 0,
      rejectedDecisionSafetyGate: 17,
      selected: 0,
      reason: 'buy_sizing_unavailable',
      reusedDecision: true,
    },
  })));

  render(<PlanPanel />);

  expect(await screen.findByText(/dimensionnement sûr du portefeuille est indisponible/i)).toBeInTheDocument();
  expect(screen.getByText(/17 candidats ne peuvent pas être exposés/i)).toBeInTheDocument();
});

it('renders a partial execution as paused in French without a dependent completion action', async () => {
  const response = plansFor('step-a', '2');
  response.plans[0].state = 'ReconciliationRequired';
  response.plans[0].currentStepOrdinal = -1;
  response.plans[0].steps[0].state = 'AwaitingConfirmation';
  response.plans[0].steps[1].state = 'RecheckRequired';
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(response)));

  render(<PlanPanel />);

  expect(await screen.findByText('Le plan doit être réconcilié avant toute autre action.')).toBeInTheDocument();
  expect(screen.getByText(/Recalcul requis/)).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Terminé' })).not.toBeInTheDocument();
  expect(screen.queryByText('Exécution différente')).not.toBeInTheDocument();
});

it('explains a moved input and required character in French without a consuming action', async () => {
  const response = plansFor('step-a', '2');
  Object.assign(response.plans[0], { state: 'RecheckRequired', holdingsEligibilityReason: 'holdings_source_changed',
    holdingsExplanation: ['Personnage requis : Personnage de test.', 'Accès requis : banque.'] });
  response.plans[0].steps[0].state = 'RecheckRequired';
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(response)));
  render(<PlanPanel />);
  expect(await screen.findByText(/Les ressources, leur localisation ou le personnage requis doivent être vérifiés/)).toBeInTheDocument();
  expect(screen.getByText('Personnage requis : Personnage de test.')).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Terminé' })).not.toBeInTheDocument();
});

function plansFor(currentStepId: string, revision: string, accountCacheScope = 'scope-a') {
  return {
    state: 'ready',
    proposals: [],
    accountCacheScope,
    plans: [{
      id: 'plan-1', attention: 'Active', state: 'InProgress', reconciliationState: 'None', currentStepOrdinal: currentStepId === 'step-a' ? 0 : 1,
      revision, modeledProfit: { copper: '100' }, committedCapital: { copper: '100' }, interactionSeconds: 30, hasUndoableEvent: false,
      steps: [
        { id: 'step-a', action: 'BuyNow', itemName: 'Objet A', quantity: 1, unitPrice: { copper: '100' }, state: currentStepId === 'step-a' ? 'Current' : 'LocallyReported' },
        { id: 'step-b', action: 'SellNow', itemName: 'Objet B', quantity: 1, unitPrice: { copper: '150' }, state: currentStepId === 'step-b' ? 'Current' : 'Pending' },
      ],
    }],
  };
}

function noPlans() { return { state: 'ready', proposals: [], plans: [], accountCacheScope: 'scope-a' }; }
function jsonResponse(payload: unknown, status = 200) {
  return { ok: status >= 200 && status < 300, status, json: vi.fn().mockResolvedValue(payload) } as unknown as Response;
}
