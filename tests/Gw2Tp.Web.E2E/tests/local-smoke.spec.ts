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

test('market dashboard loads and filters ranked sample opportunities', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Market opportunities' })).toBeVisible();
  await expect(page.getByText('Sample data', { exact: true })).toBeVisible();

  const opportunityRows = page.getByTestId('opportunity-row');
  await expect(opportunityRows).toHaveCount(4);

  await page.getByLabel(/maximum capital/i).fill('600');

  await expect(opportunityRows).toHaveCount(1);
  await expect(opportunityRows).toContainText('Sample market flip #900001');
});
