import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';

const dashboardBase = {
  state: 'ready',
  accountError: null,
  lastSuccessfulSyncAtUtc: '2026-09-19T08:00:00Z',
  currentOrdersObservedAtUtc: '2026-09-19T08:00:00Z',
  historyCoverage: { startUtc: '2026-06-01T00:00:00Z', endUtc: '2026-09-19T08:00:00Z' },
  marketState: 'available',
  isFeeRoundingExternallyVerified: false,
  realizedWindows: [
    { days: 7, status: 'supported', netProfit: { copper: '32100' }, unknownBasisQuantity: 0 },
    { days: 30, status: 'supported', netProfit: { copper: '124819' }, unknownBasisQuantity: 0 },
    { days: 90, status: 'supported', netProfit: { copper: '310200' }, unknownBasisQuantity: 0 },
  ],
  todayRealized: { days: 0, status: 'supported', netProfit: { copper: '32700' }, unknownBasisQuantity: 0 },
  openAcquisitionBasis: { copper: '0' },
  netLiquidationValue: { copper: '0' },
  unrealizedProfit: { copper: '0' },
  isOpenInventoryFullyValued: true,
  openInventory: [],
  currentBuyCapital: { copper: '0' },
  currentSellGrossValue: { copper: '0' },
  currentSellNetValue: { copper: '0' },
  currentOrders: [],
  recentTrades: [],
  bestRealizedItems: [],
  worstRealizedItems: [],
  personalLearning: null,
};

function recommendationAction(
  itemId: number,
  itemName: string,
  action = 'BUY',
  source = 'newOpportunity',
  itemIconUrl: string | null = null,
) {
  return {
    action,
    source,
    orderState: source === 'buyOrder' ? 'aboveMaximumBid' : 'notApplicable',
    orderId: source === 'buyOrder' ? String(9000 + itemId) : null,
    itemId,
    itemName,
    itemIconUrl,
    quantity: 22,
    capital: { copper: '2716032' },
    prices: {
      currentOrderUnitPrice: source === 'buyOrder' ? { copper: '123456' } : null,
      bestBuy: { copper: '118056' },
      lowestSell: { copper: '135000' },
      plannedBid: { copper: '123456' },
      plannedListPrice: { copper: '134999' },
      maximumBid: { copper: '124000' },
      immediateSalePriceRange: null,
    },
    economics: {
      acquisitionCost: { copper: '2716032' },
      grossSaleValue: { copper: '2969978' },
      listingFee: { copper: '148499' },
      exchangeFee: { copper: '296998' },
      netSaleProceeds: { copper: '2524481' },
      netProfit: { copper: '21800' },
      totalCost: { copper: '2864531' },
      roiDisplayPercent: '7.61%',
    },
    score: {
      rank: itemId,
      totalPoints: 82,
      basePoints: 85,
      appliedPenaltyPoints: 3,
      components: [{ name: 'personalEvidence', state: 'available', normalizedPercent: 80, maximumPoints: 15, awardedPoints: 9 }],
      anomalies: [],
      personalEvidence: {
        state: 'supported',
        knownBasisSampleCount: 3,
        latestKnownBasisCompletionAtUtc: '2026-09-19T07:00:00Z',
        completionRateLimitation: 'Synthetic limitation.',
      },
    },
    history: {
      confidence: 'strong',
      commonCutoffUtc: '2026-09-19T07:59:00Z',
      lastObservedAtUtc: '2026-09-19T07:58:00Z',
      windows: [
        { durationDays: 7, isAvailable: true, rawObservationCount: 24, eligibleObservationCount: 22, observedSpanPercent: 91 },
        { durationDays: 30, isAvailable: true, rawObservationCount: 80, eligibleObservationCount: 75, observedSpanPercent: 88 },
      ],
    },
    liquidity: {
      classification: 'high',
      totalBuyQuantity: 100,
      totalSellQuantity: 120,
      nearBestBuyQuantity: 30,
      nearBestSellQuantity: 40,
      participationCapQuantity: 25,
      safeLiquidationQuantity: 25,
      reasons: [],
    },
    portfolioConstraints: [
      { name: 'itemExposure', capitalCapacity: { copper: '5000000' }, quantityCapacity: 40, isBinding: false },
    ],
    reasons: [
      {
        code: action === 'CANCEL BID' ? 'bidAboveMaximum' : 'strongEvidence',
        message: 'Backend English text must not be rendered directly.',
      },
    ],
  };
}

const readyRecommendations = {
  state: 'ready',
  evidenceError: null,
  generatedAtUtc: '2026-09-19T08:01:00Z',
  lastSuccessfulSyncAtUtc: '2026-09-19T08:00:00Z',
  currentOrdersObservedAtUtc: '2026-09-19T08:00:00Z',
  scannerObservedAtUtc: '2026-09-19T08:01:00Z',
  accountEvidenceExpiresAtUtc: '2030-09-19T08:15:00Z',
  policies: {
    actionPolicyVersion: 1,
    scorePolicyVersion: 1,
    positionSizingPolicyVersion: 1,
    fifoPolicyVersion: 1,
    feePolicyVersion: 1,
    minimumProfit: { copper: '1' },
    minimumRoiBasisPoints: 0,
    cashReserveBasisPoints: 1500,
    strategy: 'FastFlip',
    category: 'TradingPost',
  },
  portfolio: {
    availableCash: { copper: '9007199254740993' },
    totalBankroll: { copper: '9007199254741993' },
    cashReserve: { copper: '1500' },
    reserveStatus: 'satisfied',
    cashReserveShortfall: { copper: '0' },
    remainingCashAfterSizing: { copper: '9007199254740678' },
  },
  actions: [
    recommendationAction(90, 'Ordre trop cher', 'CANCEL BID', 'buyOrder'),
    recommendationAction(1, "Lingot d'orichalque", 'BUY', 'newOpportunity', 'https://render.guildwars2.com/file/synthetic.png'),
    recommendationAction(2, 'Deuxième opportunité'),
    recommendationAction(3, 'Troisième opportunité'),
    recommendationAction(4, 'Quatrième opportunité'),
    recommendationAction(5, 'Cinquième opportunité'),
    recommendationAction(6, 'À attendre', 'WAIT'),
    recommendationAction(7, 'À examiner', 'REVIEW'),
    recommendationAction(8, 'Ordre à conserver', 'KEEP BID', 'buyOrder'),
    recommendationAction(9, 'Ordre dépassé à ne pas poursuivre', 'STOP BIDDING', 'buyOrder'),
  ],
};

type ApiOverrides = {
  dashboard?: unknown;
  dashboardResponses?: Array<{ ok: boolean; payload: unknown }>;
  recommendations?: unknown;
  recommendationsOk?: boolean;
  health?: unknown;
  account?: unknown;
  localData?: unknown;
};

function installFetch(overrides: ApiOverrides = {}) {
  const calls: Array<{ input: RequestInfo | URL; init?: RequestInit }> = [];
  let dashboardRequests = 0;
  const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ input, init });
    const url = String(input);

    if (url === '/api/health') {
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(overrides.health ?? { status: 'healthy' }) });
    }
    if (url === '/api/account-connection') {
      return Promise.resolve({
        ok: true,
        json: vi.fn().mockResolvedValue(overrides.account ?? {
          state: 'valid',
          grantedPermissions: ['account', 'tradingpost', 'wallet'],
          missingRequiredPermissions: [],
        }),
      });
    }
    if (url === '/api/personal-dashboard') {
      const response = overrides.dashboardResponses?.[Math.min(dashboardRequests, overrides.dashboardResponses.length - 1)];
      dashboardRequests++;
      return Promise.resolve({
        ok: response?.ok ?? true,
        json: vi.fn().mockResolvedValue(response?.payload ?? overrides.dashboard ?? dashboardBase),
      });
    }
    if (url === '/api/recommendations') {
      return Promise.resolve({
        ok: overrides.recommendationsOk ?? true,
        json: vi.fn().mockResolvedValue(overrides.recommendations ?? readyRecommendations),
      });
    }
    if (url === '/api/local-data') {
      return Promise.resolve({
        ok: true,
        json: vi.fn().mockResolvedValue(overrides.localData ?? {
          databasePath: '/synthetic/Tyrian Ledger/tyrian-ledger.db',
          backupDirectoryPath: '/synthetic/Tyrian Ledger/backups',
          managedBackups: [{ fileName: 'backup.db', createdAtUtc: '2026-09-19T07:00:00Z' }],
        }),
      });
    }
    if (url === '/api/local-data/clear-personal') {
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'personal_data_cleared' }) });
    }
    if (url === '/api/local-data/backup') {
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ fileName: 'backup.db' }) });
    }
    if (url === '/api/local-data/restore') {
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'restored', preRestoreBackupFileName: 'before-restore.db' }) });
    }
    if (url === '/api/local-data/restore-managed') {
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'restored', preRestoreBackupFileName: 'before-managed.db' }) });
    }
    if (url === '/api/personal-trading-post/sync') {
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'succeeded' }) });
    }
    if (url === '/api/diagnostics/export') {
      return Promise.resolve({ ok: true, blob: vi.fn().mockResolvedValue(new Blob(['safe diagnostic'], { type: 'text/plain' })) });
    }
    return Promise.resolve({ ok: false, json: vi.fn().mockResolvedValue({}) });
  });
  vi.stubGlobal('fetch', fetchMock);
  return { fetchMock, calls };
}

beforeEach(() => {
  installFetch();
  vi.spyOn(Storage.prototype, 'getItem');
  vi.spyOn(Storage.prototype, 'setItem');
  vi.spyOn(Storage.prototype, 'removeItem');
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('Mes Signaux MVP', () => {
  it('opens on the French Signals surface with the stable primary navigation', async () => {
    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Mes Signaux', level: 1 })).toBeInTheDocument();
    const navigation = screen.getByRole('navigation', { name: 'Navigation principale' });
    expect(within(navigation).getByRole('button', { name: 'Mes Signaux' })).toHaveAttribute('aria-current', 'page');
    expect(within(navigation).getByRole('button', { name: /Artisanat/i })).toBeDisabled();
    expect(within(navigation).getByRole('button', { name: /Réglages/i })).toBeEnabled();
    expect(screen.queryByText('Scanner')).not.toBeInTheDocument();
    expect(screen.queryByText('Investments')).not.toBeInTheDocument();
  });

  it('shows today and 30-day realized profit permanently and keeps 7/90-day values behind disclosure', async () => {
    render(<App />);

    const performance = await screen.findByLabelText('Profit réalisé');
    expect(within(performance).getByText("Aujourd'hui")).toBeInTheDocument();
    expect(within(performance).getByText('30 j')).toBeInTheDocument();
    expect(within(performance).getByText(`Données jusqu’au ${new Date(dashboardBase.historyCoverage.endUtc).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' })}`)).toBeInTheDocument();
    const disclosure = within(performance).getByText('7 j / 90 j').closest('details');
    expect(disclosure).not.toHaveAttribute('open');

    fireEvent.click(within(performance).getByText('7 j / 90 j'));
    expect(within(performance).getByText('7 jours')).toBeInTheDocument();
    expect(within(performance).getByText('90 jours')).toBeInTheDocument();
    expect(within(performance).getByLabelText(/3 pièces d'or, 27 pièces d'argent/)).toBeInTheDocument();
  });

  it('does not present retained profit as current after a dashboard refresh fails', async () => {
    cleanup();
    const { calls } = installFetch({
      dashboardResponses: [
        { ok: true, payload: dashboardBase },
        { ok: false, payload: {} },
      ],
    });
    render(<App />);
    await screen.findByLabelText('Profit réalisé');

    fireEvent.click(screen.getByRole('button', { name: 'Réglages' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Synchroniser les données du Comptoir' }));
    await waitFor(() => expect(calls.filter(call => String(call.input) === '/api/personal-dashboard')).toHaveLength(2));
    fireEvent.click(screen.getByRole('button', { name: 'Mes Signaux' }));

    const performance = await screen.findByLabelText('Profit réalisé');
    expect(within(performance).getAllByText('Indisponible')).toHaveLength(4);
    expect(within(performance).queryByText(`Données jusqu’au ${new Date(dashboardBase.historyCoverage.endUtc).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' })}`)).not.toBeInTheDocument();
  });

  it('discloses units excluded from realized profit when acquisition basis is unknown', async () => {
    cleanup();
    installFetch({
      dashboard: {
        ...dashboardBase,
        todayRealized: { ...dashboardBase.todayRealized, unknownBasisQuantity: 2 },
        realizedWindows: dashboardBase.realizedWindows.map(window =>
          window.days === 30 ? { ...window, unknownBasisQuantity: 3 } : window),
      },
    });
    render(<App />);

    expect(await screen.findByText("2 unités vendues exclues (prix d'achat inconnu)")).toBeInTheDocument();
    expect(screen.getByText("3 unités vendues exclues (prix d'achat inconnu)")).toBeInTheDocument();
  });

  it('attention-gates no-action states and prioritizes the exact Trading Post instruction', async () => {
    render(<App />);

    expect(await screen.findByText("ANNULER L'ORDRE D'ACHAT")).toBeInTheDocument();
    expect(document.querySelectorAll('.signal-list > .signal-card')).toHaveLength(6);
    expect(screen.getByText('6 signaux méritent votre attention')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Cinquième opportunité', level: 3 })).toBeInTheDocument();
    expect(screen.queryByText('À attendre')).not.toBeInTheDocument();
    expect(screen.queryByText('À examiner')).not.toBeInTheDocument();
    expect(screen.queryByText('Ordre à conserver')).not.toBeInTheDocument();
    expect(screen.queryByText('Ordre dépassé à ne pas poursuivre')).not.toBeInTheDocument();

    const buyCard = screen.getByRole('heading', { name: "Lingot d'orichalque", level: 3 }).closest('article');
    expect(buyCard).not.toBeNull();
    expect(within(buyCard!).getByText("PLACER UN ORDRE D'ACHAT")).toBeInTheDocument();
    expect(within(buyCard!).getByText('Quantité à saisir')).toBeInTheDocument();
    expect(within(buyCard!).getByText('22')).toBeInTheDocument();
    expect(within(buyCard!).getByText('Prix à saisir par unité')).toBeInTheDocument();
    expect(within(buyCard!).getByLabelText("12 pièces d'or, 34 pièces d'argent, 56 pièces de cuivre")).toBeInTheDocument();
    expect(within(buyCard!).getByText('Profit modélisé')).toBeInTheDocument();
  });

  it('renders structured French explanations without leaking backend English reason copy', async () => {
    render(<App />);

    const buyCard = (await screen.findByRole('heading', { name: "Lingot d'orichalque", level: 3 })).closest('article');
    fireEvent.click(within(buyCard!).getByText('Pourquoi ?'));

    expect(within(buyCard!).getByText('Pourquoi maintenant ?')).toBeInTheDocument();
    expect(within(buyCard!).getByText('Les fenêtres historiques disponibles soutiennent cette opportunité.')).toBeInTheDocument();
    expect(within(buyCard!).getByText(/Historique personnel : 3 résultats à base connue/)).toBeInTheDocument();
    expect(within(buyCard!).queryByText('Backend English text must not be rendered directly.')).not.toBeInTheDocument();
    expect(within(buyCard!).getByText(/Le profit est modélisé/)).toBeInTheDocument();
  });

  it('uses an item image when supplied and preserves the fallback slot on image failure', async () => {
    render(<App />);
    const buyCard = (await screen.findByRole('heading', { name: "Lingot d'orichalque", level: 3 })).closest('article');
    const image = buyCard!.querySelector('img');
    expect(image).not.toBeNull();
    fireEvent.error(image!);
    expect(within(buyCard!).getByText('◇')).toBeInTheDocument();
  });

  it('shows a deliberate zero-Signal state when only internal no-action states remain', async () => {
    cleanup();
    installFetch({
      recommendations: {
        ...readyRecommendations,
        actions: [recommendationAction(6, 'Attendre', 'WAIT'), recommendationAction(7, 'Examiner', 'REVIEW')],
      },
    });
    render(<App />);

    expect(await screen.findByText('Aucun signal ne mérite votre attention pour le moment.')).toBeInTheDocument();
    expect(screen.getByText('Les données actuelles ne justifient aucune action.')).toBeInTheDocument();
  });

  it('preserves structured degraded operational truth returned with HTTP 503', async () => {
    cleanup();
    installFetch({
      recommendationsOk: false,
      recommendations: {
        ...readyRecommendations,
        state: 'evidenceUnavailable',
        evidenceError: 'upstreamUnavailable',
        actions: [],
      },
    });
    render(<App />);

    expect(await screen.findByText(/ArenaNet ou les données de marché sont temporairement indisponibles/)).toHaveAttribute('role', 'status');
  });

  it('does not show actionable cards from a failed HTTP response claiming to be ready', async () => {
    cleanup();
    installFetch({ recommendationsOk: false, recommendations: readyRecommendations });
    render(<App />);

    expect(await screen.findByText("La lecture des signaux depuis l'application locale a échoué.")).toHaveAttribute('role', 'alert');
    expect(screen.queryByRole('heading', { name: "Lingot d'orichalque" })).not.toBeInTheDocument();
  });

  it('uses the observed history sample time in the card explanation', async () => {
    render(<App />);
    const card = (await screen.findByRole('heading', { name: "Lingot d'orichalque", level: 3 })).closest('article');
    fireEvent.click(within(card!).getByText('Pourquoi ?'));

    const history = within(card!).getByText(/Historique marché : Dernier échantillon enregistré/);
    expect(history.textContent).toContain(new Date('2026-09-19T07:58:00Z').toLocaleString('fr-FR'));
    expect(history.textContent).not.toContain(new Date('2026-09-19T07:59:00Z').toLocaleString('fr-FR'));
  });

  it('keeps degraded operational truth separate from Signal cards', async () => {
    cleanup();
    installFetch({
      recommendations: {
        ...readyRecommendations,
        state: 'evidenceUnavailable',
        evidenceError: 'upstreamUnavailable',
        actions: [],
      },
    });
    render(<App />);

    expect(await screen.findByText(/ArenaNet ou les données de marché sont temporairement indisponibles/)).toHaveAttribute('role', 'status');
    expect(screen.queryByRole('listitem')).not.toBeInTheDocument();
  });

  it('hides actions and gives an explicit state when account evidence is stale', async () => {
    cleanup();
    installFetch({
      recommendations: {
        ...readyRecommendations,
        state: 'accountEvidenceStale',
        evidenceError: 'account_evidence_stale',
        accountEvidenceExpiresAtUtc: '2026-09-19T08:15:00Z',
        actions: [],
      },
    });
    render(<App />);

    expect(await screen.findByText(/Les données du compte ont expiré/)).toHaveAttribute('role', 'status');
    expect(screen.queryByRole('listitem')).not.toBeInTheDocument();
  });

  it('expires open Signal cards at the backend-provided account-evidence deadline', async () => {
    cleanup();
    installFetch({
      recommendations: {
        ...readyRecommendations,
        accountEvidenceExpiresAtUtc: new Date(Date.now() - 1_000).toISOString(),
      },
    });
    render(<App />);

    expect(await screen.findByText(/Les données du compte ont expiré/)).toHaveAttribute('role', 'status');
    expect(screen.queryByRole('heading', { name: "Lingot d'orichalque" })).not.toBeInTheDocument();
  });

  it('uses the simulated immediate-sale range instead of the top bid for the full quantity', async () => {
    cleanup();
    installFetch({
      recommendations: {
        ...readyRecommendations,
        actions: [
          {
            ...recommendationAction(77, 'Lot à vendre', 'SELL', 'inventory'),
            prices: {
              ...recommendationAction(77, 'Lot à vendre', 'SELL', 'inventory').prices,
              immediateSalePriceRange: {
                lowestUnitPrice: { copper: '900' },
                highestUnitPrice: { copper: '1000' },
              },
            },
          },
        ],
      },
    });
    render(<App />);

    const card = (await screen.findByRole('heading', { name: 'Lot à vendre', level: 3 })).closest('article');
    expect(within(card!).getByText('Fourchette de vente immédiate par unité (modélisée)')).toBeInTheDocument();
    expect(within(card!).getByLabelText("9 pièces d'argent")).toBeInTheDocument();
    expect(within(card!).getByLabelText("10 pièces d'argent")).toBeInTheDocument();
    expect(within(card!).queryByLabelText('11 pièces d’or, 80 pièces d’argent, 56 pièces de cuivre')).not.toBeInTheDocument();
  });

  it('distinguishes account setup, permissions, and an ArenaNet outage', async () => {
    cleanup();
    installFetch({
      recommendations: {
        ...readyRecommendations,
        state: 'accountUnavailable',
        evidenceError: 'CredentialNotConfigured',
        actions: [],
      },
    });
    render(<App />);
    expect(await screen.findByText(/Aucune clé ArenaNet n'est configurée/)).toBeInTheDocument();

    cleanup();
    installFetch({
      recommendations: {
        ...readyRecommendations,
        state: 'accountUnavailable',
        evidenceError: 'Forbidden',
        actions: [],
      },
    });
    render(<App />);
    expect(await screen.findByText(/ne dispose pas des autorisations nécessaires/)).toBeInTheDocument();

    cleanup();
    installFetch({
      recommendations: {
        ...readyRecommendations,
        state: 'accountUnavailable',
        evidenceError: 'UpstreamUnavailable',
        actions: [],
      },
    });
    render(<App />);
    expect(await screen.findByText(/ArenaNet est temporairement indisponible/)).toBeInTheDocument();
  });

  it('labels source freshness explicitly without inventing a countdown', async () => {
    render(<App />);

    const freshness = await screen.findByText(/Marché actualisé/);
    expect(freshness).toBeInTheDocument();
    expect(screen.getByText('Actualisation à la demande')).toBeInTheDocument();

    fireEvent.click(freshness.closest('summary')!);
    expect(screen.getByText('Compte ArenaNet')).toBeInTheDocument();
    expect(screen.getByText('Historique marché')).toBeInTheDocument();
    expect(screen.queryByText(/prochaine actualisation dans/i)).not.toBeInTheDocument();
  });
});

describe('Réglages et sécurité locale', () => {
  it('requests diagnostic export through the protected local read contract', async () => {
    const { calls } = installFetch();
    const originalUrl = URL;
    vi.stubGlobal('URL', class extends originalUrl {
      static createObjectURL = vi.fn(() => 'blob:diagnostic');
      static revokeObjectURL = vi.fn();
    });
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Exporter le diagnostic' }));

    await waitFor(() => expect(calls.some(call => String(call.input) === '/api/diagnostics/export')).toBe(true));
    const exportCall = calls.find(call => String(call.input) === '/api/diagnostics/export');
    expect(exportCall?.init?.headers).toMatchObject({ 'X-Tyrian-Ledger-Request': '1' });
  });

  it('moves account and local recovery controls under Réglages with French displayed copy', async () => {
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));

    expect(await screen.findByRole('heading', { name: 'Réglages', level: 1 })).toBeInTheDocument();
    expect(screen.getByText('Connexion au compte prête')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Synchroniser les données du Comptoir' })).toBeEnabled();
    expect(await screen.findByRole('heading', { name: 'Données locales' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Créer une sauvegarde locale' })).toBeEnabled();
    expect(screen.getByText('Restaurer des données', { selector: 'summary' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Diagnostic' })).toBeInTheDocument();
    expect(screen.getByText('Zone sensible', { selector: 'summary' })).toBeInTheDocument();
  });

  it('requires French destructive confirmations while preserving the guarded local API contract', async () => {
    const { calls } = installFetch();
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));

    fireEvent.click(screen.getByText('Zone sensible', { selector: 'summary' }));
    const clearButton = await screen.findByRole('button', { name: 'Effacer les données personnelles' });
    expect(clearButton).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Saisissez EFFACER LES DONNÉES PERSONNELLES pour continuer'), { target: { value: 'EFFACER LES DONNÉES PERSONNELLES' } });
    expect(clearButton).toBeEnabled();
    fireEvent.click(clearButton);
    await waitFor(() => expect(calls.some(call => String(call.input) === '/api/local-data/clear-personal')).toBe(true));
    const clearCall = calls.find(call => String(call.input) === '/api/local-data/clear-personal');
    expect(JSON.parse(String(clearCall?.init?.body))).toEqual({ confirmation: 'CLEAR PERSONAL DATA' });

    fireEvent.click(screen.getByText('Restaurer des données', { selector: 'summary' }));
    const restoreButton = screen.getByRole('button', { name: 'Restaurer la sauvegarde sélectionnée' });
    expect(restoreButton).toBeDisabled();
    const file = new File(['synthetic'], 'backup.db', { type: 'application/x-sqlite3' });
    fireEvent.change(screen.getByLabelText('Fichier de sauvegarde'), { target: { files: [file] } });
    fireEvent.change(screen.getByLabelText('Saisissez RESTAURER LES DONNÉES LOCALES pour continuer'), { target: { value: 'RESTAURER LES DONNÉES LOCALES' } });
    expect(restoreButton).toBeEnabled();
    fireEvent.click(restoreButton);
    await waitFor(() => expect(calls.some(call => String(call.input) === '/api/local-data/restore')).toBe(true));
    const restoreCall = calls.find(call => String(call.input) === '/api/local-data/restore');
    const restoreBody = restoreCall?.init?.body as FormData;
    expect(restoreBody.get('confirmation')).toBe('RESTORE LOCAL DATA');
  });

  it('keeps destructive recovery actions mutually exclusive while a backup is running', async () => {
    let completeBackup!: (value: unknown) => void;
    const baseFetch = globalThis.fetch;
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input) === '/api/local-data/backup') {
        return new Promise(resolve => { completeBackup = resolve; });
      }
      return baseFetch(input, init);
    }));

    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));
    fireEvent.click(screen.getByText('Restaurer des données', { selector: 'summary' }));
    fireEvent.click(screen.getByText('Zone sensible', { selector: 'summary' }));
    const restoreFile = await screen.findByLabelText('Fichier de sauvegarde');
    fireEvent.change(restoreFile, { target: { files: [new File(['synthetic'], 'backup.db', { type: 'application/x-sqlite3' })] } });
    fireEvent.change(screen.getByLabelText('Saisissez RESTAURER LES DONNÉES LOCALES pour continuer'), { target: { value: 'RESTAURER LES DONNÉES LOCALES' } });
    fireEvent.change(screen.getByLabelText('Saisissez EFFACER LES DONNÉES PERSONNELLES pour continuer'), { target: { value: 'EFFACER LES DONNÉES PERSONNELLES' } });
    fireEvent.click(screen.getByRole('button', { name: 'Créer une sauvegarde locale' }));

    expect(await screen.findByRole('button', { name: 'Création de la sauvegarde…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Restaurer la sauvegarde sélectionnée' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Effacer les données personnelles' })).toBeDisabled();

    completeBackup({ ok: true, json: vi.fn().mockResolvedValue({ fileName: 'synthetic.db' }) });
    expect(await screen.findByText('Sauvegarde créée : synthetic.db')).toBeInTheDocument();
  });

  it('restores a selected managed backup through the guarded local route', async () => {
    const { calls } = installFetch({
      localData: {
        databasePath: '/synthetic/Tyrian Ledger/tyrian-ledger.db',
        backupDirectoryPath: '/synthetic/Tyrian Ledger/backups',
        managedBackupUploadLimitBytes: 512 * 1024 * 1024,
        managedBackups: [{ fileName: 'managed.db', createdAtUtc: '2026-09-19T07:00:00Z' }],
      },
    });
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));

    fireEvent.click(screen.getByText('Restaurer des données', { selector: 'summary' }));
    fireEvent.change(await screen.findByLabelText('Sauvegarde gérée'), { target: { value: 'managed.db' } });
    fireEvent.change(screen.getByLabelText('Saisissez RESTAURER LES DONNÉES LOCALES pour continuer'), { target: { value: 'RESTAURER LES DONNÉES LOCALES' } });
    fireEvent.click(screen.getByRole('button', { name: 'Restaurer la sauvegarde gérée' }));

    await waitFor(() => expect(calls.some(call => String(call.input) === '/api/local-data/restore-managed')).toBe(true));
    const call = calls.find(entry => String(entry.input) === '/api/local-data/restore-managed');
    expect(JSON.parse(String(call?.init?.body))).toEqual({ confirmation: 'RESTORE LOCAL DATA', backupFileName: 'managed.db' });
  });

  it('blocks oversized imported backups without inferring a managed restore', async () => {
    const { calls } = installFetch({
      localData: {
        databasePath: '/synthetic/Tyrian Ledger/tyrian-ledger.db',
        backupDirectoryPath: '/synthetic/Tyrian Ledger/backups',
        managedBackupUploadLimitBytes: 10,
        managedBackups: [{ fileName: 'backup.db', createdAtUtc: '2026-09-19T07:00:00Z' }],
      },
    });
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));

    fireEvent.click(screen.getByText('Restaurer des données', { selector: 'summary' }));
    const file = new File(['synthetic payload'], 'backup.db', { type: 'application/x-sqlite3' });
    fireEvent.change(await screen.findByLabelText('Fichier de sauvegarde'), { target: { files: [file] } });
    fireEvent.change(screen.getByLabelText('Saisissez RESTAURER LES DONNÉES LOCALES pour continuer'), { target: { value: 'RESTAURER LES DONNÉES LOCALES' } });
    fireEvent.click(screen.getByRole('button', { name: 'Restaurer la sauvegarde sélectionnée' }));

    expect(await screen.findByText(/Cette sauvegarde importée dépasse la limite locale/)).toBeInTheDocument();
    expect(calls.some(call => String(call.input) === '/api/local-data/restore')).toBe(false);
    expect(calls.some(call => String(call.input) === '/api/local-data/restore-managed')).toBe(false);
  });

  it('reports unknown restore and clear outcomes without encouraging a destructive retry', async () => {
    const baseFetch = globalThis.fetch;
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url === '/api/local-data/restore' || url === '/api/local-data/clear-personal') {
        return Promise.reject(new TypeError('connection lost'));
      }
      return baseFetch(input, init);
    }));
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));
    fireEvent.click(screen.getByText('Restaurer des données', { selector: 'summary' }));
    fireEvent.click(screen.getByText('Zone sensible', { selector: 'summary' }));

    fireEvent.change(await screen.findByLabelText('Fichier de sauvegarde'), { target: { files: [new File(['synthetic'], 'backup.db', { type: 'application/x-sqlite3' })] } });
    fireEvent.change(screen.getByLabelText('Saisissez RESTAURER LES DONNÉES LOCALES pour continuer'), { target: { value: 'RESTAURER LES DONNÉES LOCALES' } });
    fireEvent.click(screen.getByRole('button', { name: 'Restaurer la sauvegarde sélectionnée' }));
    expect(await screen.findByText("Le résultat de la restauration n'a pas pu être confirmé. Vérifiez les données locales avant de réessayer.")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Saisissez EFFACER LES DONNÉES PERSONNELLES pour continuer'), { target: { value: 'EFFACER LES DONNÉES PERSONNELLES' } });
    fireEvent.click(screen.getByRole('button', { name: 'Effacer les données personnelles' }));
    expect(await screen.findByText("Le résultat de l'effacement n'a pas pu être confirmé. Vérifiez les données locales avant de réessayer.")).toBeInTheDocument();
  });

  it('resets imported restore selection after a confirmed restore', async () => {
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));
    fireEvent.click(screen.getByText('Restaurer des données', { selector: 'summary' }));

    fireEvent.change(await screen.findByLabelText('Fichier de sauvegarde'), { target: { files: [new File(['synthetic'], 'backup.db', { type: 'application/x-sqlite3' })] } });
    fireEvent.change(screen.getByLabelText('Saisissez RESTAURER LES DONNÉES LOCALES pour continuer'), { target: { value: 'RESTAURER LES DONNÉES LOCALES' } });
    const button = screen.getByRole('button', { name: 'Restaurer la sauvegarde sélectionnée' });
    fireEvent.click(button);

    expect(await screen.findByText(/Sauvegarde restaurée/)).toBeInTheDocument();
    expect(button).toBeDisabled();
  });

  it('uses only same-origin application contracts and never browser storage', async () => {
    const { calls } = installFetch();
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));

    await waitFor(() => expect(calls.some(call => String(call.input) === '/api/local-data')).toBe(true));
    expect(calls.every(call => String(call.input).startsWith('/api/'))).toBe(true);
    expect(Storage.prototype.getItem).not.toHaveBeenCalled();
    expect(Storage.prototype.setItem).not.toHaveBeenCalled();
    expect(Storage.prototype.removeItem).not.toHaveBeenCalled();
  });

  it('shows the backend synchronization failure reason in actionable French copy', async () => {
    const baseFetch = globalThis.fetch;
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input) === '/api/personal-trading-post/sync') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'failed', error: 'unauthorized' }) });
      }
      return baseFetch(input, init);
    }));
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));

    fireEvent.click(await screen.findByRole('button', { name: 'Synchroniser les données du Comptoir' }));

    expect(await screen.findByRole('alert')).toHaveTextContent("La clé ArenaNet a été refusée. Vérifiez qu'elle est toujours valide.");
  });

  it('refreshes Signals evidence after a successful account synchronization', async () => {
    const { calls } = installFetch();
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));

    fireEvent.click(await screen.findByRole('button', { name: 'Synchroniser les données du Comptoir' }));
    await waitFor(() => expect(calls.filter(call => String(call.input) === '/api/personal-dashboard').length).toBeGreaterThan(1));

    fireEvent.click(screen.getByRole('button', { name: 'Mes Signaux' }));
    await waitFor(() => expect(calls.filter(call => String(call.input) === '/api/recommendations').length).toBeGreaterThan(1));
  });
});
