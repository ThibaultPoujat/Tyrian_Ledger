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
  recommendations?: unknown;
  recommendationsOk?: boolean;
  health?: unknown;
  account?: unknown;
  localData?: unknown;
};

function installFetch(overrides: ApiOverrides = {}) {
  const calls: Array<{ input: RequestInfo | URL; init?: RequestInit }> = [];
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
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(overrides.dashboard ?? dashboardBase) });
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
    const disclosure = within(performance).getByText('7 j / 90 j').closest('details');
    expect(disclosure).not.toHaveAttribute('open');

    fireEvent.click(within(performance).getByText('7 j / 90 j'));
    expect(within(performance).getByText('7 jours')).toBeInTheDocument();
    expect(within(performance).getByText('90 jours')).toBeInTheDocument();
    expect(within(performance).getByLabelText(/3 pièces d'or, 27 pièces d'argent/)).toBeInTheDocument();
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
  it('moves account and local recovery controls under Réglages with French displayed copy', async () => {
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));

    expect(await screen.findByRole('heading', { name: 'Réglages', level: 1 })).toBeInTheDocument();
    expect(screen.getByText('Connexion au compte prête')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Synchroniser les données du Comptoir' })).toBeEnabled();
    expect(await screen.findByRole('heading', { name: 'Sauvegarde et restauration' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Créer une sauvegarde locale' })).toBeEnabled();
  });

  it('requires French destructive confirmations while preserving the guarded local API contract', async () => {
    const { calls } = installFetch();
    render(<App />);
    fireEvent.click(screen.getByRole('button', { name: /Réglages/i }));

    const clearButton = await screen.findByRole('button', { name: 'Effacer les données personnelles' });
    expect(clearButton).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Saisissez EFFACER LES DONNÉES PERSONNELLES pour continuer'), { target: { value: 'EFFACER LES DONNÉES PERSONNELLES' } });
    expect(clearButton).toBeEnabled();
    fireEvent.click(clearButton);
    await waitFor(() => expect(calls.some(call => String(call.input) === '/api/local-data/clear-personal')).toBe(true));
    const clearCall = calls.find(call => String(call.input) === '/api/local-data/clear-personal');
    expect(JSON.parse(String(clearCall?.init?.body))).toEqual({ confirmation: 'CLEAR PERSONAL DATA' });

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
