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
  await expect(page.getByRole('heading', { name: 'The local application foundation is running.' })).toBeVisible();
  await expect(page.getByRole('status')).toHaveText('Local host connected');
  await expect(page.getByRole('button')).toHaveCount(0);
  expect(apiRequests).toEqual(['/api/health']);
  expect(externalRequests).toEqual([]);
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
