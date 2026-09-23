import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import PlanPanel from './PlanPanel';

afterEach(() => vi.unstubAllGlobals());

it('cancels an unperformed step and exposes the refreshed proposal for restart', async () => {
  const responses = [
    {
      state: 'ready',
      proposals: [],
      plans: [{
        id: 'plan-1', attention: 'Active', state: 'InProgress', reconciliationState: 'None', currentStepOrdinal: 0,
        modeledProfit: { copper: '100' }, committedCapital: { copper: '100' }, interactionSeconds: 30, hasUndoableEvent: false,
        steps: [{ id: 'step-1', action: 'BuyNow', itemName: 'Objet', quantity: 1, unitPrice: { copper: '100' }, state: 'Current' }],
      }],
    },
    {
      state: 'ready',
      proposals: [{
        id: 'plan-1', attention: 'Active', modeledProfit: { copper: '100' }, committedCapital: { copper: '100' }, interactionSeconds: 30,
        steps: [{ id: 'step-1', action: 'BuyNow', itemName: 'Objet', quantity: 1, unitPrice: { copper: '100' }, state: 'Pending' }],
      }],
      plans: [],
    },
  ];
  let reads = 0;
  const fetchMock = vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'cancelled' }) });
    return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(responses[reads++]) });
  });
  vi.stubGlobal('fetch', fetchMock);

  render(<PlanPanel />);
  fireEvent.click(await screen.findByRole('button', { name: 'Action non effectuée' }));

  await waitFor(() => expect(screen.getByRole('button', { name: 'Démarrer' })).toBeInTheDocument());
  expect(fetchMock).toHaveBeenCalledWith('/api/plans/plan-1/complete', expect.objectContaining({ method: 'POST' }));
});
