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

it('explains that recipes with incomplete market evidence are excluded without inventing prices', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: vi.fn().mockResolvedValue({ state: 'NoOpportunities', truncationReasons: [], summaryExclusions: ['MissingInputEvidence'], opportunities: [] }) }));
  render(<CraftingPanel />);
  expect(await screen.findByText('Certaines recettes n’ont pas toutes les preuves de marché nécessaires pour leurs ingrédients ou leur sortie. Elles restent exclues : aucun prix, profondeur ou profit n’est inventé.')).toBeInTheDocument();
});

it('reports a successful unchanged crafting refresh through the protected local endpoint', async () => {
  const fetchMock = vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'succeeded', changed: false, capturedAtUtc: '2026-09-24T00:00:00Z', error: null, bank: { availability: 'Available', count: 1, error: null }, materials: { availability: 'Available', count: 1, error: null }, recipes: { availability: 'Available', count: 1, error: null }, crafting: { availability: 'Available', count: 1, error: null } }) });
    return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'NoOpportunities', truncationReasons: [], summaryExclusions: [], opportunities: [] }) });
  });
  vi.stubGlobal('fetch', fetchMock);
  render(<CraftingPanel />);

  fireEvent.click((await screen.findAllByRole('button', { name: 'Actualiser les données d’artisanat' })).at(-1)!);

  await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/account-crafting/refresh', expect.objectContaining({ method: 'POST', headers: { 'X-Tyrian-Ledger-Request': '1' } })));
  expect(await screen.findByText('Vérification terminée : les données d’artisanat sont inchangées.')).toBeInTheDocument();
});

it('keeps the view responsive and explains a failed crafting refresh', async () => {
  const fetchMock = vi.fn((_input: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') return Promise.resolve({ ok: false, json: vi.fn().mockResolvedValue({ outcome: 'failed', changed: null, capturedAtUtc: null, error: 'rate_limited', bank: null, materials: null, recipes: null, crafting: null }) });
    return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'NoOpportunities', truncationReasons: [], summaryExclusions: [], opportunities: [] }) });
  });
  vi.stubGlobal('fetch', fetchMock);
  render(<CraftingPanel />);

  fireEvent.click((await screen.findAllByRole('button', { name: 'Actualiser les données d’artisanat' })).at(-1)!);

  expect(await screen.findByText('ArenaNet limite temporairement l’actualisation. Réessayez plus tard.')).toBeInTheDocument();
  expect(screen.getAllByRole('button', { name: 'Actualiser les données d’artisanat' }).at(-1)).toBeEnabled();
});
