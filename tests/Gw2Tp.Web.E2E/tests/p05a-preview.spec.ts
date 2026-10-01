import { mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { expect, test, type TestInfo } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const viewport = { width: 1920, height: 1080 };

async function captureActual(page: import('@playwright/test').Page, testInfo: TestInfo, fileName: string) {
  const evidenceDirectory = process.env.TYRIAN_LEDGER_P05A_EVIDENCE_DIR;
  const outputPath = evidenceDirectory === undefined
    ? testInfo.outputPath(fileName)
    : join(resolve(evidenceDirectory), fileName);
  if (evidenceDirectory !== undefined) mkdirSync(resolve(evidenceDirectory), { recursive: true });
  const image = await page.screenshot({ path: outputPath, animations: 'disabled', fullPage: false });
  expect(image.readUInt32BE(16)).toBe(1920);
  expect(image.readUInt32BE(20)).toBe(1080);
}

async function openScenario(page: import('@playwright/test').Page, scenario: string) {
  await page.setViewportSize(viewport);
  await page.goto('/preview.html?scenario=' + scenario);
  await expect(page.getByText('Démonstration · données fictives').first()).toBeVisible();
}

test('renders three normal Signal cards with separate estimated profit and released cash', async ({ page }, testInfo) => {
  await openScenario(page, 'normal');

  await expect(page.getByRole('heading', { name: 'Signaux', level: 1 })).toBeVisible();
  await expect(page.locator('.p05a-signal-card')).toHaveCount(3);
  await expect(page.getByRole('heading', { name: 'Vendre votre surplus' })).toBeVisible();
  await expect(page.getByText('Or récupérable', { exact: true })).toBeVisible();
  await expect(page.getByText('profit non calculé')).toBeVisible();
  await expect(page.getByText('À vérifier maintenant')).toHaveCount(0);
  await captureActual(page, testInfo, 'signaux-normal-1920x1080.png');

  const accessibility = await new AxeBuilder({ page }).analyze();
  expect(accessibility.violations).toEqual([]);
});

test('shows an urgent band only in the urgent fixture', async ({ page }, testInfo) => {
  await openScenario(page, 'urgent');

  await expect(page.getByRole('heading', { name: 'Une offre engagée dépasse votre limite' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Examiner l’offre' })).toBeEnabled();
  await expect(page.locator('.p05a-signal-card')).toHaveCount(3);
  await captureActual(page, testInfo, 'signaux-urgent-1920x1080.png');
});

test('shows no qualifying Signal and honest stale/partial account coverage', async ({ page }, testInfo) => {
  await openScenario(page, 'degraded');

  await expect(page.getByRole('heading', { name: 'Aucun signal ne mérite votre attention pour le moment.' })).toBeVisible();
  await expect(page.locator('.p05a-status-coverage')).toHaveText('Couverture partielle · 2 personnages sur 5');
  await expect(page.getByText(/l’état de l’équipement ne sont pas couverts/)).toBeVisible();
  await expect(page.locator('.p05a-signal-card')).toHaveCount(0);
  await captureActual(page, testInfo, 'signaux-vide-couverture-partielle-1920x1080.png');
});

test('opens the session drawer over Signaux, applies a deadline objective, and makes no network requests', async ({ page }, testInfo) => {
  const apiRequests: string[] = [];
  const externalRequests: string[] = [];
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.pathname === '/api' || url.pathname.startsWith('/api/')) apiRequests.push(request.method() + ' ' + url.pathname);
    if (!['127.0.0.1', 'localhost', '[::1]'].includes(url.hostname) && url.protocol.startsWith('http')) {
      externalRequests.push(request.url());
    }
  });

  await openScenario(page, 'normal');
  await page.getByRole('button', { name: 'Adapter ma session' }).click();
  const dialog = page.getByRole('dialog', { name: 'Adapter ma session' });
  await expect(dialog).toBeVisible();
  await expect(dialog.locator('.p05a-allocation-summary')).toContainText('Plafond');
  await expect(dialog.locator('.p05a-allocation-summary')).toContainText('Déjà engagé');
  await expect(dialog.locator('.p05a-allocation-summary')).toContainText('Disponible');
  await captureActual(page, testInfo, 'session-drawer-1920x1080.png');

  const accessibility = await new AxeBuilder({ page }).analyze();
  expect(accessibility.violations).toEqual([]);

  await dialog.getByRole('button', { name: /Or disponible avant/ }).click();
  await expect(dialog.getByText(/Une vente future à délai incertain ne devient pas de l’or disponible/)).toBeVisible();
  await dialog.getByRole('button', { name: 'Appliquer à cette session' }).click();
  await expect(page.locator('.p05a-signal-card')).toHaveCount(1);
  await expect(page.getByRole('heading', { name: 'Vendre votre surplus' })).toBeVisible();
  await expect(page.getByText('Objectif : or récupérable avant l’échéance')).toBeVisible();

  expect(apiRequests).toEqual([]);
  expect(externalRequests).toEqual([]);
});

test('cancels edits on Escape, restores focus, validates inputs, and keeps saved defaults in memory only', async ({ page }) => {
  await openScenario(page, 'normal');
  const trigger = page.getByRole('button', { name: 'Adapter ma session' });
  await trigger.click();

  const dialog = page.getByRole('dialog');
  const close = dialog.getByRole('button', { name: 'Fermer et annuler les changements' });
  await expect(close).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(dialog.getByRole('button', { name: 'Annuler', exact: true })).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(close).toBeFocused();

  const duration = dialog.getByLabel('Durée personnalisée en minutes');
  await duration.fill('5');
  await page.keyboard.press('Escape');
  await expect(dialog).toHaveCount(0);
  await expect(trigger).toBeFocused();
  await expect(page.getByRole('button', { name: '15 min' })).toHaveAttribute('aria-pressed', 'true');

  await trigger.click();
  const reopened = page.getByRole('dialog');
  await expect(reopened.getByLabel('Durée personnalisée en minutes')).toHaveValue('15');
  await reopened.getByLabel('Durée personnalisée en minutes').fill('0');
  await reopened.getByRole('button', { name: 'Appliquer à cette session' }).click();
  await expect(reopened.getByRole('alert')).toContainText('Vérifiez la durée');
  await reopened.getByLabel('Durée personnalisée en minutes').fill('5');
  await reopened.getByRole('button', { name: /Enregistrer comme préférence habituelle/ }).click();
  await expect(reopened.getByRole('status')).toContainText('uniquement en mémoire de démonstration');
  expect(await page.evaluate(() => window.localStorage.length === 0)).toBe(true);
  await expect(page.locator('.p05a-segment--selected')).toContainText('15 min');
});

