import { expect, test, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const snapshotFixture = readFileSync(
  resolve(__dirname, '../../fixtures/market-snapshots/market-snapshot-v1.json'),
  'utf8',
);
const freshNow = Date.parse('2026-09-01T12:30:00.000Z');

async function fulfillSnapshot(page: Page, body = snapshotFixture, status = 200, now = freshNow) {
  await page.addInitScript((fixedNow) => { Date.now = () => fixedNow; }, now);
  await page.route('**/market-snapshot.json', (route) => route.fulfill({
    status,
    contentType: 'application/json',
    body,
  }));
}

async function completeSetup(page: Page, gold = '12') {
  await page.getByRole('button', { name: 'Set up capital and risk' }).click();
  await page.getByRole('textbox', { name: 'Gold', exact: true }).fill(gold);
  await page.getByRole('radio', { name: /Balanced/ }).check();
  await page.getByRole('button', { name: 'Save preferences' }).click();
}

test('uses one fresh static snapshot to calculate recommendations locally without API traffic', async ({ page }) => {
  const prohibitedRequests: string[] = [];
  await fulfillSnapshot(page);
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.pathname === '/api' || url.pathname.startsWith('/api/') || url.hostname.endsWith('guildwars2.com')) {
      prohibitedRequests.push(request.url());
    }
  });

  await page.goto('/');
  await expect(page).toHaveTitle('Tyrian Ledger | Unofficial Guild Wars 2 fan site');
  await expect(page.getByRole('status')).toContainText('Compatible snapshot loaded.');
  await expect(page.getByRole('status')).toContainText('Data age: 30 minutes.');
  await expect(page.getByText('Tyrian Ledger is an unofficial, independent Guild Wars 2 fan site and is not affiliated with or endorsed by ArenaNet or NCSOFT.')).toBeVisible();
  await completeSetup(page);

  await expect(page.getByRole('heading', { name: 'Synthetic public item' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Place an order and wait' })).toBeVisible();
  await expect(page.getByText('12g 0s 0c')).toBeVisible();
  await expect(page.getByRole('button', { name: /scan/i })).toHaveCount(0);
  expect(prohibitedRequests).toEqual([]);
});

test('marks a delayed static snapshot non-actionable and shows no recommendations', async ({ page }) => {
  await fulfillSnapshot(page, snapshotFixture, 200, Date.parse('2026-09-01T12:30:00.001Z'));
  await page.goto('/');
  await completeSetup(page);

  await expect(page.getByRole('alert')).toContainText('Snapshot refresh is delayed.');
  await expect(page.getByRole('alert')).toContainText('30 minutes old');
  await expect(page.getByRole('heading', { name: 'Synthetic public item' })).toHaveCount(0);
});

test('shows malformed and unavailable static snapshot states without cards', async ({ page }) => {
  await fulfillSnapshot(page, '{not JSON');
  await page.goto('/');
  await expect(page.getByRole('alert')).toContainText('not valid JSON');
  await expect(page.getByRole('heading', { name: 'Synthetic public item' })).toHaveCount(0);

  await page.unroute('**/market-snapshot.json');
  await page.route('**/market-snapshot.json', (route) => route.fulfill({ status: 404, contentType: 'application/json', body: '{}' }));
  await page.reload();
  await expect(page.getByRole('alert')).toContainText('snapshot is unavailable');
  await expect(page.getByRole('heading', { name: 'Synthetic public item' })).toHaveCount(0);
});

test('keyboard setup and fresh static recommendations meet WCAG 2.2 AA automated checks', async ({ page }) => {
  await fulfillSnapshot(page);
  await page.goto('/');

  await page.getByRole('button', { name: 'Set up capital and risk' }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Set your trading guardrails' })).toBeVisible();
  await page.getByRole('textbox', { name: 'Gold', exact: true }).fill('12');
  await page.getByRole('radio', { name: /Balanced/ }).check();
  await page.getByRole('button', { name: 'Save preferences' }).click();
  await expect(page.getByRole('heading', { name: 'Synthetic public item' })).toBeVisible();

  await page.getByRole('button', { name: 'View manual trade plan' }).click();
  await expect(page.getByRole('dialog', { name: 'Synthetic public item' })).toContainText('Follow this checklist manually');
  await expect(page.getByRole('button', { name: 'Close manual trade plan' })).toBeFocused();

  await expect(new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'])
    .analyze()).resolves.toMatchObject({ violations: [] });

  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog')).toHaveCount(0);
});

test('keeps the companion view within a narrow mobile viewport', async ({ page }) => {
  await fulfillSnapshot(page);
  await page.setViewportSize({ width: 375, height: 720 });
  await page.goto('/');
  await completeSetup(page);

  await page.getByRole('button', { name: 'View manual trade plan' }).click();
  await expect(page.getByRole('dialog', { name: 'Synthetic public item' })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(await page.locator('.opportunity-detail').evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    return bounds.left >= 0 && bounds.right <= window.innerWidth;
  })).toBe(true);
});

test('uses a project Pages base path and hash routes that survive a reload', async ({ page }) => {
  await fulfillSnapshot(page);
  await page.goto('/Tyrian_Ledger/');
  await expect(page).toHaveURL(/\/Tyrian_Ledger\/$/);

  await page.getByRole('link', { name: 'Settings' }).click();
  await expect(page).toHaveURL(/\/Tyrian_Ledger\/#\/settings$/);
  await expect(page.getByRole('heading', { name: 'Set your trading guardrails' })).toBeVisible();

  await page.reload();
  await expect(page.getByRole('heading', { name: 'Set your trading guardrails' })).toBeVisible();
});
