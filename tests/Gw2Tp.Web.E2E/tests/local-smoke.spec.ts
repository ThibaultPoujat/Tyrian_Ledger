import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const apiPort = process.env.TYRIAN_LEDGER_E2E_API_PORT ?? '5010';
const apiBaseUrl = `http://127.0.0.1:${apiPort}`;

test.afterEach(async ({ request }) => {
  const response = await request.put(`${apiBaseUrl}/api/preferences/user-session`, {
    data: {
      capitalLimitCopper: null,
      minimumProfitCopper: null,
      riskPreference: 'all',
      strategyPreference: 'all',
      allocationPercent: 100,
    },
  });
  expect(response.status()).toBe(200);
});

test('local API health endpoint responds', async ({ page }) => {
  const response = await page.goto(`${apiBaseUrl}/healthz`);

  if (response === null) {
    throw new Error('The local health endpoint did not return a response.');
  }

  expect(response.status()).toBe(200);
  await expect(page.locator('body')).toContainText('ok');
});

test('browser API traffic never includes a credential', async ({ page }) => {
  const syntheticCredential = 'synthetic-gw2-api-credential-for-browser-boundary-audit';
  const inspectedResponses: string[] = [];

  const statusResponse = await page.request.get(`${apiBaseUrl}/api/status`);
  const statusBody = await statusResponse.text();

  expect(statusResponse.ok()).toBeTruthy();
  expect(statusBody).toContain('"credentialStatus":"configured"');
  expect(statusBody).not.toContain(syntheticCredential);

  await page.route('**/api/account/access', (route) => route.fulfill({
    contentType: 'application/json',
    body: JSON.stringify({
      validationStatus: 'notconfigured',
      keyId: null,
      keyName: null,
      permissions: [],
      features: [],
    }),
  }));

  page.on('request', (request) => {
    if (request.url().includes('/api/')) {
      expect(request.url()).not.toContain(syntheticCredential);
      expect(request.headers().authorization).toBeUndefined();
      expect(request.postData() ?? '').not.toContain(syntheticCredential);
    }
  });

  page.on('response', async (response) => {
    if (!response.url().includes('/api/')) {
      return;
    }

    inspectedResponses.push(await response.text());
  });

  await page.goto('/');
  await expect(page.getByText('Sample data', { exact: true })).toBeVisible();

  await expect.poll(() => inspectedResponses.length).toBeGreaterThan(0);
  expect(inspectedResponses.join('\n')).not.toContain(syntheticCredential);
});

test('market dashboard saves local preferences and filters ranked sample opportunities', async ({ page, request }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Market opportunities' })).toBeVisible();
  await expect(page.getByText('Sample data', { exact: true })).toBeVisible();
  await expect(page.getByTestId('personal-history')).toContainText('No local operation history yet.');
  await expect(page.getByTestId('personal-history')).toContainText('unknown lifetime history is not backfilled');

  const opportunityRows = page.getByTestId('opportunity-row');
  await expect(opportunityRows).toHaveCount(5);

  await page.getByLabel(/available capital/i).fill('1200');
  await page.getByLabel(/per-opportunity allocation/i).fill('50');
  await page.getByRole('button', { name: 'Save and apply preferences' }).click();

  await expect(opportunityRows).toHaveCount(1);
  await expect(opportunityRows).toContainText('Sample market flip #900001');

  await page.reload();
  await expect(page.getByLabel(/available capital/i)).toHaveValue('1200');
  await expect(opportunityRows).toHaveCount(1);

  const resetResponse = await request.put(`${apiBaseUrl}/api/preferences/user-session`, {
    data: {
      capitalLimitCopper: null,
      minimumProfitCopper: null,
      riskPreference: 'all',
      strategyPreference: 'all',
      allocationPercent: 100,
    },
  });
  expect(resetResponse.status()).toBe(200);
});

test('market dashboard filters the session shortlist by explicit effort category without promising a duration', async ({ page }) => {
  await page.goto('/');

  await page.getByLabel(/session effort/i).selectOption('high');

  const opportunityRows = page.getByTestId('opportunity-row');
  await expect(opportunityRows).toHaveCount(1);
  await expect(opportunityRows).toContainText('Sample market flip #900005');
  await expect(page.getByText(/rough planning labels, not time, execution, fill, or profit guarantees/i)).toBeVisible();
});

test('opportunity detail explains the modeled scenario without implying an actual outcome', async ({ page }) => {
  await page.goto('/');

  await page.getByRole('button', { name: 'View details for Sample market flip #900004' }).click();

  const detail = page.getByTestId('opportunity-detail');
  await expect(detail.getByRole('heading', { name: 'Sample market flip #900004' })).toBeVisible();
  await expect(detail).toContainText('Modeled scenario only.');
  await expect(detail).toContainText('not an actual purchase, sale, fill, fee, or realized-profit outcome');
  await expect(detail).toContainText('Human-readable calculation breakdown');
  await expect(detail).toContainText('Order-book impact and liquidity');
  await expect(detail).toContainText('Data age');
});

test('keyboard users can complete preference, detail, and confirmation flows with visible focus', async ({ page }) => {
  await page.goto('/');

  const capital = page.getByLabel(/available capital/i);
  await capital.focus();
  await expect(capital).toBeFocused();
  await capital.fill('1200');

  const allocation = page.getByLabel(/per-opportunity allocation/i);
  await allocation.focus();
  await allocation.fill('50');
  await allocation.press('Enter');
  await expect(page.getByTestId('opportunity-row')).toHaveCount(1);

  const detailTrigger = page.getByRole('button', { name: 'View details for Sample market flip #900001' });
  await detailTrigger.focus();
  await detailTrigger.press('Enter');

  const closeDetails = page.getByRole('button', { name: 'Close details' });
  await expect(closeDetails).toBeFocused();
  await closeDetails.press('Enter');
  await expect(page.getByTestId('opportunity-detail')).toHaveCount(0);
  await expect(detailTrigger).toBeFocused();

  const clearTrigger = page.getByRole('button', { name: 'Clear account snapshot data' });
  await clearTrigger.focus();
  await clearTrigger.press('Space');
  const confirmClear = page.getByRole('button', { name: 'Confirm clear account snapshots' });
  await expect(confirmClear).toBeFocused();
  await page.getByRole('button', { name: 'Cancel' }).press('Enter');
  await expect(clearTrigger).toBeFocused();

  await clearTrigger.press('Space');
  await page.getByRole('button', { name: 'Confirm clear account snapshots' }).press('Enter');
  await expect(page.getByText(/account snapshot data cleared/i)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Clear account snapshot data' })).toBeFocused();
});

test('dashboard and expanded detail meet WCAG 2.2 AA automated checks', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Market opportunities' })).toBeVisible();

  await expect(new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
    .analyze()).resolves.toMatchObject({ violations: [] });

  await page.getByRole('button', { name: 'View details for Sample market flip #900004' }).click();
  await expect(page.getByTestId('opportunity-detail')).toBeVisible();

  await expect(new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
    .analyze()).resolves.toMatchObject({ violations: [] });
});
