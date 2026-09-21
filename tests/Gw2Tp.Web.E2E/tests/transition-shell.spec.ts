import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test('loads Mes Signaux by default and keeps account/recovery controls under Réglages', async ({ page }) => {
  const apiRequests: string[] = [];
  const externalRequests: string[] = [];
  page.on('request', request => {
    const url = new URL(request.url());
    if (url.pathname === '/api' || url.pathname.startsWith('/api/')) apiRequests.push(url.pathname);
    if (!['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname) && url.protocol.startsWith('http')) {
      externalRequests.push(request.url());
    }
  });

  await page.goto('/');

  await expect(page).toHaveTitle('Tyrian Ledger | Assistant personnel de profit');
  await expect(page.getByRole('heading', { name: 'Mes Signaux', level: 1 })).toBeVisible();
  const navigation = page.getByRole('navigation', { name: 'Navigation principale' });
  await expect(navigation.getByRole('button', { name: 'Mes Signaux' })).toHaveAttribute('aria-current', 'page');
  await expect(navigation.getByRole('button', { name: /Artisanat/i })).toBeDisabled();
  await expect(navigation.getByRole('button', { name: 'Réglages' })).toBeEnabled();
  await expect(page.getByText('Application locale connectée')).toBeVisible();

  await navigation.getByRole('button', { name: 'Réglages' }).click();
  await expect(page.getByRole('heading', { name: 'Réglages', level: 1 })).toBeVisible();
  await expect(page.getByText('Aucune clé ArenaNet configurée')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Données locales' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Créer une sauvegarde locale' })).toBeVisible();

  expect(apiRequests).toEqual(expect.arrayContaining([
    '/api/health', '/api/account-connection', '/api/personal-dashboard', '/api/recommendations', '/api/local-data',
  ]));
  expect(externalRequests).toEqual([]);
});

test('exports local diagnostics from Réglages through the protected read', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Réglages' }).click();

  const downloadPromise = page.waitForEvent('download');
  await page.getByRole('button', { name: 'Exporter le diagnostic' }).click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBe('tyrian-ledger-diagnostic.txt');
});

test('presents the execution-first Signal flow at 1920x1080 using only mocked local evidence', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  const externalRequests: string[] = [];
  page.on('request', request => {
    const url = new URL(request.url());
    if (!['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname) && url.protocol.startsWith('http')) {
      externalRequests.push(request.url());
    }
  });
  await page.route('**/api/recommendations', route => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify(mockRecommendations()),
  }));

  await page.goto('/');

  await expect(page.getByText('7 signaux méritent votre attention')).toBeVisible();
  const firstSignal = page.locator('.signal-card').first();
  await expect(firstSignal.getByText("ANNULER L'ORDRE D'ACHAT", { exact: true })).toBeVisible();
  await expect(firstSignal.getByRole('heading', { name: 'Ordre trop cher' })).toBeVisible();
  await expect(firstSignal.getByText('Quantité concernée')).toBeVisible();
  await expect(firstSignal.getByText('22')).toBeVisible();
  await expect(firstSignal.getByText("Prix actuel de l'ordre")).toBeVisible();
  await expect(firstSignal.getByLabel("12 pièces d'or, 34 pièces d'argent, 56 pièces de cuivre")).toBeVisible();
  await expect(firstSignal.getByText('Capital à libérer')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Sixième opportunité', level: 3 })).toBeVisible();
  await expect(page.getByText('À attendre')).toHaveCount(0);

  const sidebarBefore = await page.locator('.signals-sidebar').boundingBox();
  const performanceBefore = await page.locator('.performance-summary').boundingBox();

  const why = firstSignal.getByText('Pourquoi ?');
  await why.focus();
  await why.press('Enter');
  await expect(why).toHaveAttribute('aria-label', "Masquer l'explication de ce signal");
  await expect(firstSignal.getByRole('heading', { name: 'Pourquoi maintenant ?' })).toBeVisible();
  await expect(firstSignal.getByText(/L’ordre actuel dépasse le prix maximal autorisé/)).toBeVisible();
  await expect(firstSignal.getByText(/Le profit est modélisé/)).toBeVisible();

  const sidebarAfter = await page.locator('.signals-sidebar').boundingBox();
  const performanceAfter = await page.locator('.performance-summary').boundingBox();
  expect(sidebarAfter?.x).toBe(sidebarBefore?.x);
  expect(sidebarAfter?.y).toBe(sidebarBefore?.y);
  expect(performanceAfter?.x).toBe(performanceBefore?.x);
  expect(performanceAfter?.y).toBe(performanceBefore?.y);

  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(externalRequests).toEqual([]);
});

test('keeps the primary shell fixed across normal, zero and degraded states at 1920x1080', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  let response = mockRecommendations();
  await page.route('**/api/recommendations', route => route.fulfill({
    contentType: 'application/json',
    status: response.state === 'evidenceUnavailable' ? 503 : 200,
    body: JSON.stringify(response),
  }));

  await page.goto('/');
  await expect(page.getByText('7 signaux méritent votre attention')).toBeVisible();
  const normalLayout = await primaryLayout(page);

  response = { ...mockRecommendations(), actions: [] };
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Aucun signal ne mérite votre attention pour le moment.' })).toBeVisible();
  await expectPrimaryLayout(page, normalLayout);

  response = {
    ...mockRecommendations(),
    state: 'evidenceUnavailable',
    evidenceError: 'upstream_unavailable',
    actions: [],
  };
  await page.reload();
  await expect(page.getByText(/ArenaNet ou les données de marché sont temporairement indisponibles/)).toBeVisible();
  await expectPrimaryLayout(page, normalLayout);
});

test('keeps the Signals workspace accessible', async ({ page }) => {
  await page.route('**/api/recommendations', route => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify(mockRecommendations()),
  }));
  await page.goto('/');

  await expect(new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
    .analyze()).resolves.toMatchObject({ violations: [] });
});

test('keeps the primary navigation and execution fields within a narrow viewport', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 720 });
  await page.route('**/api/recommendations', route => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify(mockRecommendations()),
  }));
  await page.goto('/');

  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  const firstSignal = page.locator('.signal-card').first();
  const bounds = await firstSignal.boundingBox();
  expect(bounds?.x ?? -1).toBeGreaterThanOrEqual(0);
  expect((bounds?.x ?? 0) + (bounds?.width ?? 0)).toBeLessThanOrEqual(375);
});

test('keeps managed local backup recovery usable from Réglages on a narrow viewport', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 720 });
  await page.goto('/');
  await page.getByRole('button', { name: 'Réglages' }).click();

  const createBackup = page.getByRole('button', { name: 'Créer une sauvegarde locale' });
  await expect(createBackup).toBeEnabled();
  await createBackup.click();
  await expect(page.getByText(/^Sauvegarde créée :/)).toBeVisible();

  const managedBackup = page.getByLabel('Sauvegarde gérée');
  await expect.poll(async () => (await managedBackup.locator('option').count()) > 1).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

type PrimaryLayout = {
  sidebar: { x: number; y: number };
  performance: { x: number; y: number };
};

async function primaryLayout(page: import('@playwright/test').Page): Promise<PrimaryLayout> {
  const sidebar = await page.locator('.signals-sidebar').boundingBox();
  const performance = await page.locator('.performance-summary').boundingBox();
  expect(sidebar).not.toBeNull();
  expect(performance).not.toBeNull();
  return {
    sidebar: { x: sidebar!.x, y: sidebar!.y },
    performance: { x: performance!.x, y: performance!.y },
  };
}

async function expectPrimaryLayout(page: import('@playwright/test').Page, expected: PrimaryLayout) {
  const actual = await primaryLayout(page);
  expect(actual.sidebar.x).toBe(expected.sidebar.x);
  expect(actual.sidebar.y).toBe(expected.sidebar.y);
  expect(actual.performance.x).toBe(expected.performance.x);
  expect(actual.performance.y).toBe(expected.performance.y);
}

function mockRecommendations() {
  const action = (
    itemId: number,
    itemName: string,
    recommendationAction = 'BUY',
    source = 'newOpportunity',
  ) => ({
    action: recommendationAction,
    source,
    orderState: source === 'buyOrder' ? 'aboveMaximumBid' : 'notApplicable',
    orderId: source === 'buyOrder' ? String(9000 + itemId) : null,
    itemId,
    itemName,
    itemIconUrl: null,
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
    score: { rank: itemId, totalPoints: 82, basePoints: 85, appliedPenaltyPoints: 3, components: [], anomalies: [], personalEvidence: null },
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
    portfolioConstraints: [{ name: 'itemExposure', capitalCapacity: { copper: '5000000' }, quantityCapacity: 40, isBinding: false }],
    reasons: [{
      code: recommendationAction === 'CANCEL BID' ? 'bidAboveMaximum' : 'strongEvidence',
      message: 'Backend English copy is intentionally not rendered directly.',
    }],
  });

  return {
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
      action(90, 'Ordre trop cher', 'CANCEL BID', 'buyOrder'),
      action(1, "Lingot d'orichalque"),
      action(2, 'Deuxième opportunité'),
      action(3, 'Troisième opportunité'),
      action(4, 'Quatrième opportunité'),
      action(5, 'Cinquième opportunité'),
      action(6, 'Sixième opportunité'),
      action(7, 'À attendre', 'WAIT'),
    ],
  };
}
