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
  await expect(page.getByText('Market scan status', { exact: true })).toBeVisible();

  await expect.poll(() => inspectedResponses.length).toBeGreaterThan(0);
  expect(inspectedResponses.join('\n')).not.toContain(syntheticCredential);
});

test('market dashboard saves local preferences for the bounded live scan', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Market opportunities' })).toBeVisible();
  await expect(page.getByText('Market scan status', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'No tracked market items yet' })).toBeVisible();
  await expect(page.getByTestId('personal-history')).toContainText('No local operation history yet.');
  await expect(page.getByTestId('personal-history')).toContainText('unknown lifetime history is not backfilled');

  await page.getByLabel(/available capital/i).fill('1200');
  await page.getByLabel(/per-opportunity allocation/i).fill('50');
  await page.getByLabel(/analysis quantity/i).fill('2');
  await page.getByLabel(/listing fee/i).fill('500');
  await page.getByLabel(/listing rounding/i).selectOption('down');
  await page.getByLabel(/exchange fee/i).fill('1000');
  await page.getByLabel(/exchange rounding/i).selectOption('up');
  await page.getByRole('button', { name: 'Save and apply preferences' }).click();

  await expect(page.getByText('Preferences saved. Updating ranked opportunities.')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'No tracked market items yet' })).toBeVisible();

  await page.reload();
  await expect(page.getByLabel(/available capital/i)).toHaveValue('1200');
  await expect(page.getByLabel(/analysis quantity/i)).toHaveValue('2');
  await expect(page.getByLabel(/listing fee/i)).toHaveValue('500');
  await expect(page.getByLabel(/exchange rounding/i)).toHaveValue('up');
});

test('market dashboard has no effort filter and makes no profit claim before fee configuration', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByLabel(/session effort/i)).toHaveCount(0);
  await expect(page.getByTestId('opportunity-row')).toHaveCount(0);
  await expect(page.getByText(/no locally tracked market items are available to scan/i)).toBeVisible();
});

test('an empty tracked list does not expose a modeled opportunity detail', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'No tracked market items yet' })).toBeVisible();
  await expect(page.getByRole('button', { name: /view details for/i })).toHaveCount(0);
  await expect(page.getByTestId('opportunity-detail')).toHaveCount(0);
});

test('keyboard users can complete preference and confirmation flows with visible focus', async ({ page }) => {
  await page.goto('/');

  const capital = page.getByLabel(/available capital/i);
  await capital.focus();
  await expect(capital).toBeFocused();
  await capital.fill('1200');

  const quantity = page.getByLabel(/analysis quantity/i);
  await quantity.focus();
  await quantity.fill('2');
  await quantity.press('Enter');
  await expect(page.getByRole('heading', { name: 'No tracked market items yet' })).toBeVisible();

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

test('empty dashboard meets WCAG 2.2 AA automated checks', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Market opportunities' })).toBeVisible();

  await expect(new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
    .analyze()).resolves.toMatchObject({ violations: [] });

});
