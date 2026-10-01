import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

test.use({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });

for (const scenario of ['bank', 'own-bag', 'partial-bank', 'other-bag', 'bound-other', 'protected', 'paused-location']) {
  test('renders protected holdings admission: ' + scenario, async ({ page }) => {
    const payload = readFileSync(resolve('../../docs/ux/evidence/TKT-M22-P02C/fixtures', scenario + '.json'), 'utf8');
    const paused = scenario === 'paused-location';
    await page.route(paused ? '**/api/plans' : '**/api/crafting-opportunities',
      route => route.fulfill({ contentType: 'application/json', body: payload }));
    await page.goto('/');
    const navigation = page.getByRole('button', { name: paused ? 'Plans' : 'Artisanat', exact: true });
    await navigation.focus();
    await expect(navigation).toBeFocused();
    await page.keyboard.press('Enter');
    const ready = ['bank', 'own-bag', 'partial-bank'].includes(scenario);
    if (paused) {
      await expect(page.getByText(/Les ressources, leur localisation ou le personnage requis doivent être vérifiés/)).toBeVisible();
      await expect(page.getByRole('button', { name: 'Terminé', exact: true })).toHaveCount(0);
    } else if (ready) {
      await expect(page.getByRole('heading', { name: 'Actions d’artisanat à considérer' })).toBeVisible();
      await expect(page.getByText('Personnage requis : Personnage A de test.')).toBeVisible();
      const start = page.getByRole('button', { name: 'Démarrer ce plan' });
      await start.focus();
      await expect(start).toBeFocused();
    } else {
      await expect(page.getByRole('heading', { name: 'Aucun parcours d’artisanat viable' })).toBeVisible();
      await expect(page.getByRole('button', { name: 'Démarrer ce plan' })).toHaveCount(0);
    }
    expect((await new AxeBuilder({ page }).include('main').analyze()).violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    const capture = process.env.TYRIAN_LEDGER_P02C_SCREENSHOT_DIR;
    if (capture) await page.screenshot({ path: resolve(capture, scenario + '-1920x1080.png'), animations: 'disabled', fullPage: false });
    await page.evaluate(() => { document.body.style.zoom = '1.25'; });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}
