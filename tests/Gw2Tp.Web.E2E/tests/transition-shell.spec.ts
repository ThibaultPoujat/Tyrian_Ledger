import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

test('explains the local-first transition without contacting an API or publishing recommendations', async ({ page }) => {
  const prohibitedRequests: string[] = [];
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.pathname === '/api' || url.pathname.startsWith('/api/') || url.hostname.endsWith('guildwars2.com')) {
      prohibitedRequests.push(request.url());
    }
  });

  await page.goto('/');

  await expect(page).toHaveTitle('Tyrian Ledger | Local-first personal trading assistant');
  await expect(page.getByRole('heading', { name: 'The public trading assistant has been retired.' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'What remains' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'What comes next' })).toBeVisible();
  await expect(page.getByRole('button')).toHaveCount(0);
  expect(prohibitedRequests).toEqual([]);
});

test('keeps the transition shell accessible', async ({ page }) => {
  await page.goto('/');

  await expect(new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
    .analyze()).resolves.toMatchObject({ violations: [] });
});

test('keeps the transition shell within a narrow mobile viewport', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 720 });
  await page.goto('/');

  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(await page.locator('.transition-panel').evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    return bounds.left >= 0 && bounds.right <= window.innerWidth;
  })).toBe(true);
});
