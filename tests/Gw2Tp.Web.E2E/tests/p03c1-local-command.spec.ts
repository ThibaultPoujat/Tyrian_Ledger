import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

test.use({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
test('revision-bound local undo retains the existing French plan surface', async ({ page }) => {
  const fixtures = resolve('../../docs/ux/evidence/TKT-M22-P03C1/fixtures');
  const reported = JSON.parse(readFileSync(resolve(fixtures, 'reported.json'), 'utf8'));
  const undone = JSON.parse(readFileSync(resolve(fixtures, 'undone.json'), 'utf8'));
  let current = reported;
  let commands = 0;
  await page.route('**/api/plans', route => route.fulfill({ json: current }));
  await page.route('**/api/plans/*/undo', route => {
    expect(route.request().postDataJSON()).toEqual({ expectedRevision: reported.plans[0].revision });
    expect(route.request().headers()['x-tyrian-ledger-account-view-scope']).toBe(reported.accountCacheScope);
    commands++; current = undone;
    return route.fulfill({ json: { state: 'undone', plan: undone.plans[0] } });
  });
  await page.goto('/');
  const navigation = page.getByRole('button', { name: 'Plans', exact: true });
  await navigation.focus(); await page.keyboard.press('Enter');
  const undo = page.getByRole('button', { name: 'Annuler la dernière étape', exact: true });
  await expect(undo).toBeVisible(); await undo.focus(); await expect(undo).toBeFocused();
  expect((await new AxeBuilder({ page }).include('main').analyze()).violations).toEqual([]);
  const destination = process.env.TYRIAN_LEDGER_P03C1_SCREENSHOT_DIR;
  if (destination) await page.screenshot({ path: resolve(destination, 'reported-1920x1080.png'), animations: 'disabled' });
  await page.keyboard.press('Enter');
  await expect(page.getByRole('button', { name: 'Terminé', exact: true })).toBeVisible();
  expect(commands).toBe(1); await expect(undo).toHaveCount(0);
  expect((await new AxeBuilder({ page }).include('main').analyze()).violations).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  if (destination) await page.screenshot({ path: resolve(destination, 'undone-1920x1080.png'), animations: 'disabled' });
  await page.evaluate(() => { document.body.style.zoom = '1.25'; });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
