import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import CraftingPanel from './CraftingPanel';

afterEach(() => vi.unstubAllGlobals());

it('renders a French active craft path and starts it through the shared Plans endpoint', async () => {
  const fetchMock = vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'started' }) });
    return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({
      state: 'Ready', truncationReasons: [], summaryExclusions: [], opportunities: [{
        id: 'craft:1', outputName: 'Insigne', outputIconUrl: null, outputQuantity: 2,
        economics: { netProfit: { copper: '900' }, totalCost: { copper: '1000' }, state: 'Available' }, attention: 'Active', interactionSeconds: 90,
        isActionable: true, exclusions: [], procurementExplanation: ['Le coût complet est inférieur à l’achat direct.'], planId: 'craft:1',
        steps: [{ action: 'BuyNow', itemName: 'Minerai', quantity: 2, unitPrice: { copper: '50' } }, { action: 'Craft', itemName: 'Insigne', quantity: 2, unitPrice: null }],
      }],
    }) });
  });
  vi.stubGlobal('fetch', fetchMock);
  render(<CraftingPanel />);
  expect(await screen.findByRole('heading', { name: 'Actions d’artisanat à considérer' })).toBeInTheDocument();
  expect(screen.getByText('Insigne')).toBeInTheDocument();
  fireEvent.click(screen.getByText('Pourquoi ?'));
  expect(screen.getByText('Approvisionnement et faisabilité')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Démarrer ce plan' }));
  await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/plans/craft%3A1/start', expect.objectContaining({ method: 'POST', headers: { 'X-Tyrian-Ledger-Request': '1' } })));
});

it('explains no viable opportunity and explicit search truncation in French', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: vi.fn().mockResolvedValue({ state: 'NoOpportunities', truncationReasons: ['WorkLimit'], summaryExclusions: ['WeakHistory'], opportunities: [] }) }));
  render(<CraftingPanel />);
  expect(await screen.findByRole('heading', { name: 'Aucun parcours d’artisanat viable' })).toBeInTheDocument();
});
