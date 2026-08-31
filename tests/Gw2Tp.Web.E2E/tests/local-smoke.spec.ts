import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

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

test('M9 shell exposes only Recommendations and Settings', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByRole('navigation', { name: 'Primary navigation' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Recommendations' })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('link', { name: 'Settings' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Recommendations' })).toBeVisible();
  await expect(page.getByText(/market opportunities|crafting analysis|personal history/i)).toHaveCount(0);
});

test('retired API and browser routes are unavailable', async ({ page, request }) => {
  for (const retiredPath of [
    '/api/status',
    '/api/account/access',
    '/api/account/snapshots',
    '/api/market-research/watchlist',
    '/api/history/statistics',
    '/api/dashboard/opportunities',
  ]) {
    expect((await request.get(`${apiBaseUrl}${retiredPath}`)).status()).toBe(404);
  }

  await page.goto('/history');
  await expect(page.getByTestId('unavailable-route')).toContainText('Route unavailable');
});

test('browser shell does not initiate API traffic', async ({ page }) => {
  const apiRequests: string[] = [];

  page.on('request', (request) => {
    if (request.url().includes('/api/')) {
      apiRequests.push(request.url());
      expect(request.headers().authorization).toBeUndefined();
    }
  });

  await page.goto('/');

  expect(apiRequests).toEqual([]);
});

test('M9 shell meets WCAG 2.2 AA automated checks', async ({ page }) => {
  await page.goto('/settings');
  await expect(page.getByRole('heading', { name: 'Settings' })).toBeVisible();

  await expect(new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
    .analyze()).resolves.toMatchObject({ violations: [] });
});
