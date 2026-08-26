import { expect, test } from '@playwright/test';

test('local API health endpoint responds', async ({ page }) => {
  const response = await page.goto('http://127.0.0.1:5000/healthz');

  if (response === null) {
    throw new Error('The local health endpoint did not return a response.');
  }

  expect(response.status()).toBe(200);
  await expect(page.locator('body')).toContainText('ok');
});

test('Tyrian Ledger React shell renders', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Tyrian Ledger' })).toBeVisible();
  await expect(page.getByText('Local skeleton running. Market features arrive in later milestones.')).toBeVisible();
});
