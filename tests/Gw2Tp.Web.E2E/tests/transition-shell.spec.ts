import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test('loads the React shell and health contract from the local host without external requests', async ({ page }) => {
  const apiRequests: string[] = [];
  const externalRequests: string[] = [];
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.pathname === '/api' || url.pathname.startsWith('/api/')) {
      apiRequests.push(url.pathname);
    }
    if (!['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname) && url.protocol.startsWith('http')) {
      externalRequests.push(request.url());
    }
  });

  await page.goto('/');

  await expect(page).toHaveTitle('Tyrian Ledger | Local-first personal trading assistant');
  await expect(page.getByRole('heading', { name: 'What should I do?' })).toBeVisible();
  await expect(page.getByText('Local host connected')).toBeVisible();
  await expect(page.getByText('No ArenaNet key configured')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Connect an account to view your dashboard' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Backup and recovery' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Create local backup' })).toBeVisible();
  expect(apiRequests).toEqual(expect.arrayContaining(['/api/health', '/api/account-connection', '/api/local-data', '/api/personal-dashboard', '/api/recommendations']));
  expect(externalRequests).toEqual([]);
});

test('reviews the primary action within two minutes using only mocked local evidence', async ({ page }) => {
  const startedAt = Date.now();
  const httpRequests: string[] = [];
  page.on('request', request => {
    if (request.url().startsWith('http')) httpRequests.push(request.url());
  });
  await page.route('**/api/recommendations', route => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify(mockRecommendations()),
  }));

  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'What should I do?' })).toBeVisible();
  await expect(page.getByText('7 manual actions ready to review.')).toBeVisible();
  const firstAction = page.locator('.recommendation-card').first();
  await expect(firstAction.getByText('CANCEL BID', { exact: true })).toBeVisible();
  await expect(firstAction.getByRole('heading', { name: 'Overpriced bid' })).toBeVisible();
  await expect(firstAction.getByText('0g 1s 25c', { exact: true })).toBeVisible();
  await expect(firstAction.getByText('0g 1s 10c', { exact: true })).toBeVisible();
  await expect(firstAction.getByText(/Confidence Strong/)).toBeVisible();
  await expect(firstAction.getByText(/Constraint Item Exposure/)).toBeVisible();
  await expect(firstAction.locator('.recommendation-reason')).toContainText(/current bid is above the maximum/i);
  await expect(page.getByText('Sixth opportunity')).toHaveCount(0);

  const evidenceSummary = firstAction.getByText('Review depth, history, score, and reasons');
  await evidenceSummary.focus();
  await evidenceSummary.press('Enter');
  await expect(firstAction.getByRole('heading', { name: 'Retained history' })).toBeVisible();
  await expect(firstAction.getByText(/7 days: 22\/24 eligible/)).toBeVisible();
  await page.getByRole('button', { name: 'Show 1 more new opportunities' }).click();
  await expect(page.getByText('Sixth opportunity')).toBeVisible();

  await page.setViewportSize({ width: 375, height: 720 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await expect(new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
    .analyze()).resolves.toMatchObject({ violations: [] });
  expect(Date.now() - startedAt).toBeLessThan(120_000);
  expect(httpRequests.filter(url => url.includes('/api/recommendations'))).toHaveLength(1);
  expect(httpRequests.some(url => url.includes('api.guildwars2.com'))).toBe(false);
  expect(httpRequests.filter(url => !['127.0.0.1', 'localhost', '[::1]'].includes(new URL(url).hostname))).toEqual([]);
});

test('keeps the local runtime shell accessible', async ({ page }) => {
  await page.goto('/');

  await expect(new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
    .analyze()).resolves.toMatchObject({ violations: [] });
});

test('keeps the local runtime shell within a narrow mobile viewport', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 720 });
  await page.goto('/');

  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(await page.locator('.transition-panel').evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    return bounds.left >= 0 && bounds.right <= window.innerWidth;
  })).toBe(true);
});

test('keeps a populated managed-backup inventory within a narrow mobile viewport', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 720 });
  await page.goto('/');

  const createBackup = page.getByRole('button', { name: 'Create local backup' });
  await expect(createBackup).toBeEnabled();
  await createBackup.click();
  await expect(page.getByText(/^Backup created:/)).toBeVisible();

  const managedBackup = page.getByLabel('Managed backup');
  await expect.poll(async () => (await managedBackup.locator('option').count()) > 1).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(await managedBackup.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    return bounds.left >= 0 && bounds.right <= window.innerWidth;
  })).toBe(true);
});

function mockRecommendations() {
  const action = (itemId: number, itemName: string, recommendationAction = 'BUY', source = 'newOpportunity') => ({
    action: recommendationAction,
    source,
    orderState: source === 'buyOrder' ? 'aboveMaximumBid' : 'notApplicable',
    orderId: source === 'buyOrder' ? String(9000 + itemId) : null,
    itemId,
    itemName,
    quantity: 3,
    capital: { copper: '315' },
    prices: {
      currentOrderUnitPrice: source === 'buyOrder' ? { copper: '125' } : null,
      bestBuy: { copper: '99' },
      lowestSell: { copper: '151' },
      plannedBid: { copper: '100' },
      plannedListPrice: { copper: '150' },
      maximumBid: { copper: '110' },
    },
    economics: {
      acquisitionCost: { copper: '300' }, grossSaleValue: { copper: '450' },
      listingFee: { copper: '23' }, exchangeFee: { copper: '45' }, netSaleProceeds: { copper: '382' },
      netProfit: { copper: '82' }, totalCost: { copper: '323' }, roiDisplayPercent: '25.39%',
    },
    score: { rank: itemId, totalPoints: 82, basePoints: 85, appliedPenaltyPoints: 3, components: [], anomalies: [] },
    history: {
      confidence: 'strong', commonCutoffUtc: '2026-09-09T12:00:00Z',
      windows: [
        { durationDays: 7, isAvailable: true, rawObservationCount: 24, eligibleObservationCount: 22, observedSpanPercent: 91 },
        { durationDays: 30, isAvailable: true, rawObservationCount: 80, eligibleObservationCount: 75, observedSpanPercent: 88 },
      ],
    },
    liquidity: {
      classification: 'high', totalBuyQuantity: 100, totalSellQuantity: 120,
      nearBestBuyQuantity: 30, nearBestSellQuantity: 40, participationCapQuantity: 10,
      safeLiquidationQuantity: 10, reasons: [],
    },
    portfolioConstraints: [{ name: 'itemExposure', capitalCapacity: { copper: '500' }, quantityCapacity: 5, isBinding: true }],
    reasons: [{
      code: recommendationAction === 'CANCEL BID' ? 'bidAboveMaximum' : 'strongEvidence',
      message: recommendationAction === 'CANCEL BID'
        ? 'The current bid is above the maximum allowed bid.'
        : 'Both retained-history windows support this opportunity.',
    }],
  });
  return {
    state: 'ready', evidenceError: null, generatedAtUtc: '2026-09-09T12:00:00Z',
    lastSuccessfulSyncAtUtc: '2026-09-09T11:59:00Z', currentOrdersObservedAtUtc: '2026-09-09T11:58:00Z', scannerObservedAtUtc: '2026-09-09T11:57:00Z',
    policies: {
      actionPolicyVersion: 1, scorePolicyVersion: 1, positionSizingPolicyVersion: 1,
      fifoPolicyVersion: 1, feePolicyVersion: 1, minimumProfit: { copper: '1' },
      minimumRoiBasisPoints: 0, cashReserveBasisPoints: 1500, strategy: 'FastFlip', category: 'TradingPost',
    },
    portfolio: {
      availableCash: { copper: '9007199254740993' }, totalBankroll: { copper: '9007199254741993' },
      cashReserve: { copper: '1500' }, reserveStatus: 'satisfied', cashReserveShortfall: { copper: '0' },
      remainingCashAfterSizing: { copper: '9007199254740678' },
    },
    actions: [
      action(90, 'Overpriced bid', 'CANCEL BID', 'buyOrder'),
      action(1, 'Top opportunity'), action(2, 'Second opportunity'), action(3, 'Third opportunity'),
      action(4, 'Fourth opportunity'), action(5, 'Fifth opportunity'), action(6, 'Sixth opportunity'),
    ],
  };
}
