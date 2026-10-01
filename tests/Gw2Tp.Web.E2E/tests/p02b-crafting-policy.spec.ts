import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

test.use({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });

for (const scenario of ['rejected-mixed', 'rejected-inactive', 'eligible-single-actor']) {
  test('renders the real policy response: ' + scenario, async ({ page }) => {
    const evidence = resolve('../../docs/ux/evidence/TKT-M22-P02B');
    const payload = readFileSync(resolve(evidence, 'fixtures', scenario + '.json'), 'utf8');
    await page.route('**/api/crafting-opportunities', route => route.fulfill({ contentType: 'application/json', body: payload }));
    await page.goto('/');
    const navigation = page.getByRole('button', { name: 'Artisanat', exact: true });
    await navigation.focus();
    await expect(navigation).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: 'Artisanat', level: 1 })).toBeVisible();
    if (scenario === 'eligible-single-actor') {
      await expect(page.getByRole('heading', { name: 'Actions d’artisanat à considérer' })).toBeVisible();
      const start = page.getByRole('button', { name: 'Démarrer ce plan' });
      await expect(start).toBeVisible();
      await start.focus();
      await expect(start).toBeFocused();
    } else {
      await expect(page.getByRole('heading', { name: 'Aucun parcours d’artisanat viable' })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Démarrer ce plan' })).toHaveCount(0);
      const refresh = page.getByRole('button', { name: 'Actualiser les données d’artisanat' });
      await refresh.focus();
      await expect(refresh).toBeFocused();
    }
    const accessibility = await new AxeBuilder({ page }).include('main').analyze();
    expect(accessibility.violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    const capture = process.env.TYRIAN_LEDGER_P02B_SCREENSHOT_DIR;
    if (capture) await page.screenshot({ path: resolve(capture, scenario + '-1920x1080.png'), animations: 'disabled', fullPage: false });
    await page.evaluate(() => { document.body.style.zoom = '1.25'; });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
