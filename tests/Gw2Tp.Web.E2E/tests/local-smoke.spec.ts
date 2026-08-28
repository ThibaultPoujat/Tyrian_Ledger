import { expect, test } from '@playwright/test';

const apiPort = process.env.TYRIAN_LEDGER_E2E_API_PORT ?? '5010';
const apiBaseUrl = `http://127.0.0.1:${apiPort}`;

test('local API health endpoint responds', async ({ page }) => {
  const response = await page.goto(`${apiBaseUrl}/healthz`);

  if (response === null) {
    throw new Error('The local health endpoint did not return a response.');
  }

  expect(response.status()).toBe(200);
  await expect(page.locator('body')).toContainText('ok');
});

test('market dashboard saves local preferences and filters ranked sample opportunities', async ({ page, request }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Market opportunities' })).toBeVisible();
  await expect(page.getByText('Sample data', { exact: true })).toBeVisible();

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
