import { expect, test, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const running = {
  state: 'running',
  progress: { stage: 'reading-finalist-listings', finalistCount: 2 },
  isRetryable: false,
  result: null,
};

const cancelled = {
  state: 'cancelled',
  progress: null,
  isRetryable: true,
  result: null,
};

const rateLimited = {
  state: 'rate-limited',
  progress: null,
  isRetryable: true,
  result: null,
};

const complete = {
  state: 'complete',
  progress: null,
  isRetryable: false,
  result: {
    capitalCopper: 123456,
    riskProfile: 'balanced',
    spendCapCopper: 30864,
    scanCompletedAtUtc: '2026-08-31T16:00:00Z',
    canActNow: [card(1, 'can-act-now')],
    placeOrderAndWait: [card(2, 'place-order-and-wait')],
  },
};

function card(rank: number, route: 'can-act-now' | 'place-order-and-wait') {
  return {
    rank,
    itemId: rank,
    itemName: `Browser item ${rank}`,
    route,
    quantity: 2,
    buyUnitPriceCopper: 1000,
    saleUnitPriceCopper: 2000,
    buyOrderReserveCopper: 2000,
    grossSaleCopper: 4000,
    listingFeeCopper: 200,
    exchangeFeeCopper: 400,
    netSaleProceedsCopper: 3400,
    totalCostCopper: 2200,
    modeledProfitCopper: 1400,
    modeledRoi: { profitCopper: 1400, totalCostCopper: 2200 },
    scanCompletedAtUtc: '2026-08-31T16:00:00Z',
    routeEvidence: {
      sellerQuantityAtOrBelowBuyPrice: route === 'can-act-now' ? 2 : 1,
      coversSelectedQuantity: route === 'can-act-now',
    },
    assumptions: [
      'current-order-book-snapshot-only',
      'manual-in-game-orders-required',
      'no-execution-sale-or-profit-guarantee',
    ],
  };
}

async function fulfillJson(page: Page, payload: unknown, status = 200) {
  await page.route('**/api/recommendations/scan', (route) => route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(payload),
  }));
}

async function completeSetup(page: Page) {
  await page.getByRole('button', { name: 'Set up my capital and risk' }).click();
  await page.getByRole('textbox', { name: 'Gold', exact: true }).fill('12');
  await page.getByRole('textbox', { name: 'Silver', exact: true }).fill('34');
  await page.getByRole('textbox', { name: 'Copper', exact: true }).fill('56');
  await page.getByRole('radio', { name: /Balanced/ }).check();
  await page.getByRole('button', { name: 'Save settings' }).click();
}

test('first visit guides setup through a successful manual-market scan', async ({ page }) => {
  await page.route('**/api/recommendations/scan', async (route) => {
    if (route.request().method() === 'POST') {
      await route.fulfill({ status: 202, contentType: 'application/json', body: JSON.stringify(running) });
      return;
    }

    await route.fulfill({ contentType: 'application/json', body: JSON.stringify(complete) });
  });
  await page.goto('/');

  await expect(page.getByText('A short guided setup')).toBeVisible();
  await expect(page.getByText(/always create every buy order and sell listing yourself/i)).toBeVisible();
  await completeSetup(page);
  await expect(page.getByText('12g 34s 56c')).toBeVisible();

  await page.getByRole('button', { name: 'Scan the market' }).click();
  await expect(page.getByRole('status')).toContainText('2 finalists need detailed checks');
  await expect(page.getByRole('heading', { name: 'Can act now' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Place an order and wait' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Browser item 1' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Manual in-game steps' }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: /copy/i })).toHaveCount(0);
});

test('cancelling a running scan removes recommendations and offers an explicit retry', async ({ page }) => {
  await page.route('**/api/recommendations/scan', async (route) => {
    const method = route.request().method();
    if (method === 'POST') {
      await route.fulfill({ status: 202, contentType: 'application/json', body: JSON.stringify(running) });
      return;
    }
    if (method === 'DELETE') {
      await route.fulfill({ contentType: 'application/json', body: JSON.stringify(cancelled) });
      return;
    }
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify(running) });
  });
  await page.goto('/');
  await completeSetup(page);

  await page.getByRole('button', { name: 'Scan the market' }).click();
  await page.getByRole('button', { name: 'Cancel scan' }).click();

  await expect(page.getByRole('alert')).toContainText('Scan cancelled. No recommendations were kept.');
  await expect(page.getByRole('heading', { name: /Browser item/ })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Retry scan' })).toBeVisible();
});

test('a rate-limit outcome stays empty until the player retries', async ({ page }) => {
  let scanAttempts = 0;
  await page.route('**/api/recommendations/scan', async (route) => {
    scanAttempts += 1;
    const payload = scanAttempts === 1 ? rateLimited : complete;
    await route.fulfill({
      status: scanAttempts === 1 ? 202 : 202,
      contentType: 'application/json',
      body: JSON.stringify(payload),
    });
  });
  await page.goto('/');
  await completeSetup(page);

  await page.getByRole('button', { name: 'Scan the market' }).click();
  await expect(page.getByRole('alert')).toContainText('asked us to slow down');
  await expect(page.getByRole('heading', { name: /Browser item/ })).toHaveCount(0);

  await page.getByRole('button', { name: 'Retry scan' }).click();
  await expect(page.getByRole('heading', { name: 'Browser item 1' })).toBeVisible();
  expect(scanAttempts).toBe(2);
});

test('keyboard setup flow and completed recommendations pass WCAG 2.2 AA automated checks', async ({ page }) => {
  await fulfillJson(page, complete, 202);
  await page.goto('/');

  await page.getByRole('button', { name: 'Set up my capital and risk' }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Settings' })).toBeVisible();
  await page.getByRole('textbox', { name: 'Gold', exact: true }).fill('12');
  await page.getByRole('textbox', { name: 'Silver', exact: true }).fill('34');
  await page.getByRole('textbox', { name: 'Copper', exact: true }).fill('56');
  await page.getByRole('radio', { name: /Balanced/ }).check();
  await page.getByRole('button', { name: 'Save settings' }).click();
  await page.getByRole('button', { name: 'Scan the market' }).click();
  await expect(page.getByRole('heading', { name: 'Browser item 1' })).toBeVisible();
  await expect(new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
    .analyze()).resolves.toMatchObject({ violations: [] });
});
