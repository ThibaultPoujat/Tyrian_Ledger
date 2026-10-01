import { mkdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { expect, test, type Page, type TestInfo } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

async function open(page: Page, scenario: string) {
  await page.goto('/preview.html?scenario=' + scenario);
  await expect(page.getByRole('button', { name: 'Plans', exact: true })).toHaveAttribute('aria-current', 'page');
}
async function capture(page: Page, info: TestInfo, name: string) {
  const directory = process.env.TYRIAN_LEDGER_P05B_EVIDENCE_DIR;
  if (directory) mkdirSync(resolve(directory), { recursive: true });
  const path = directory ? join(resolve(directory), name + '-1920x1080.png') : info.outputPath(name + '-1920x1080.png');
  const image = await page.screenshot({ path, fullPage: false, animations: 'disabled' });
  expect(image.readUInt32BE(16)).toBe(1920);
  expect(image.readUInt32BE(20)).toBe(1080);
  expect(await page.evaluate(() => window.devicePixelRatio)).toBe(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  await expect(page.locator('.p05a-status-bar')).toBeInViewport();
  await expect(page.getByRole('button', { name: 'Réglages', exact: true })).toBeInViewport();
}

test('comparison and alternative selection retain resource-free previews and the reference proportions', async ({ page }, info) => {
  await open(page, 'comparison');
  await expect(page.locator('.p05b-plan-choice')).toHaveCount(3);
  await expect(page.getByRole('button', { name: 'Comparer Artisanat ciblé' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByText(/Les plans peuvent partager des ressources/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Démarrer ce plan' })).toBeInViewport();
  const layout = await page.locator('.p05b-comparison').boundingBox();
  const alternatives = await page.locator('.p05b-alternatives').boundingBox();
  expect(alternatives!.width / layout!.width).toBeGreaterThan(0.67);
  expect(alternatives!.width / layout!.width).toBeLessThan(0.72);
  await capture(page, info, 'comparison');
  expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
  const alternate = page.getByRole('button', { name: 'Comparer Ventes immédiates' });
  await alternate.focus(); await page.keyboard.press('Enter');
  await expect(alternate).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('complementary', { name: 'Récapitulatif du plan sélectionné' })).toContainText('Vendre et collecter');
  await capture(page, info, 'alternate-selected');
});

for (const scenario of ['no-plan', 'insufficient-capital', 'stale']) {
  test('unavailable comparison: ' + scenario, async ({ page }, info) => {
    await open(page, scenario);
    await expect(page.getByRole('heading', { name: 'Aucun plan admissible' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Démarrer ce plan' })).toHaveCount(0);
    await capture(page, info, scenario);
    await page.getByRole('button', { name: 'Adapter ma session' }).click();
    await expect(page.getByRole('dialog', { name: 'Adapter ma session' })).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('button', { name: 'Adapter ma session' })).toBeFocused();
  });
}

for (const scenario of ['active', 'provisional', 'wait', 'partial', 'contradiction', 'long']) {
  test('execution visual and provider eligibility: ' + scenario, async ({ page }, info) => {
    await open(page, scenario);
    const instruction = page.getByRole('region', { name: 'Instruction actuelle' });
    const title = instruction.locator('h3');
    await expect(title).toBeFocused();
    await expect(page.locator('[aria-current="step"]')).toBeInViewport({ ratio: 0.5 });
    await expect(instruction.getByText(/Personnage :/)).toBeVisible();
    await expect(instruction.getByText(/Lieu :/)).toBeVisible();
    expect(await instruction.locator('input').count()).toBe(0);
    const report = page.getByRole('button', { name: 'J’ai effectué cette étape' });
    if (['wait', 'partial', 'contradiction'].includes(scenario)) await expect(report).toBeDisabled();
    else await expect(report).toBeEnabled();
    if (scenario === 'provisional') await expect(page.getByText(/aucun délai fixe n’est imposé/)).toBeVisible();
    if (scenario === 'wait') await expect(page.getByText(/sans garantie de fraîcheur/)).toBeVisible();
    if (scenario === 'partial' || scenario === 'contradiction') await expect(page.locator('.p05b-coverage--complete')).toHaveCount(0);
    await capture(page, info, scenario);
    expect((await new AxeBuilder({ page }).analyze()).violations).toEqual([]);
    if (scenario === 'long') {
      await expect(page.locator('.p05b-phase-scroll details')).toHaveCount(4);
      for (const details of await page.locator('.p05b-phase-scroll details').all()) {
        if (await details.getAttribute('open') === null) await details.locator('summary').click();
      }
      await expect(page.locator('.p05b-phase-scroll li')).toHaveCount(24);
      const last = page.locator('.p05b-phase-scroll li').last();
      await last.scrollIntoViewIfNeeded(); await expect(last).toBeInViewport();
      await expect(title).toBeInViewport();
    }
  });
}

test('combined four-screen flow, keyboard start, copy, observed completion, local report and guidance never mutate production', async ({ page, context }) => {
  const forbidden: string[] = [];
  page.on('request', request => {
    const url = new URL(request.url());
    if (url.pathname.startsWith('/api') || (url.protocol.startsWith('http') && !['127.0.0.1', 'localhost'].includes(url.hostname))) forbidden.push(request.method() + ' ' + request.url());
  });
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);
  await page.goto('/preview.html?scenario=normal');
  await page.getByRole('button', { name: 'Adapter ma session' }).click();
  await page.getByRole('button', { name: /Or disponible avant/ }).click();
  await page.getByRole('button', { name: 'Appliquer à cette session' }).click();
  await page.getByRole('button', { name: 'Voir le détail' }).click();
  await expect(page.locator('.p05b-plan-choice')).toHaveCount(1);
  await expect(page.getByRole('heading', { name: /Récupérer de l’or sous/ })).toBeVisible();
  await page.getByRole('button', { name: 'Démarrer ce plan' }).focus();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Lingot d’orichalque' })).toBeFocused();
  await page.getByRole('button', { name: 'Copier le nom' }).click();
  await expect(page.getByText('Nom copié.')).toBeVisible();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe('Lingot d’orichalque');
  await page.getByRole('button', { name: 'Actualiser l’observation' }).click();
  await expect(page.getByText('Total instruit')).toBeVisible();
  await expect(page.getByText('Frais modélisés')).toBeVisible();
  await page.getByRole('button', { name: 'J’ai effectué cette étape' }).click();
  await expect(page.getByRole('heading', { name: 'Or issu du surplus' })).toBeFocused();
  await page.getByRole('button', { name: 'Annuler la déclaration locale' }).click();
  await expect(page.getByRole('heading', { name: 'Lingot d’orichalque' })).toBeFocused();
  await page.getByRole('button', { name: 'Mettre le guidage en pause' }).click();
  await expect(page.getByRole('button', { name: 'J’ai effectué cette étape' })).toBeDisabled();
  await page.getByRole('button', { name: 'Reprendre le guidage' }).click();
  await expect(page.getByRole('button', { name: 'J’ai effectué cette étape' })).toBeEnabled();
  await page.getByRole('button', { name: 'Un problème avec cette étape ?' }).click();
  await expect(page.getByText(/Si l’action diffère, suspendez le guidage/)).toBeVisible();
  expect(forbidden).toEqual([]);
  expect(await page.evaluate(() => localStorage.length + sessionStorage.length)).toBe(0);
});

test('lost acknowledgement recovers the original step and account switching invalidates it', async ({ page }) => {
  await open(page, 'lost-ack');
  await page.getByRole('button', { name: 'J’ai effectué cette étape' }).dblclick();
  await expect(page.getByRole('alert')).toContainText('perdu');
  await page.getByRole('button', { name: 'Actualiser', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Composant fictif' })).toBeFocused();
  await page.getByText('Compte de démonstration', { exact: true }).click();
  await page.getByRole('button', { name: 'Changer de compte fictif' }).click();
  await expect(page.getByRole('alert')).toContainText('anciennes déclarations sont invalidées');
  await expect(page.getByRole('heading', { name: 'Composant fictif' })).toHaveCount(0);
});

test('comparison and exact instructions remain reachable at 150%-zoom-like viewport', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 });
  for (const scenario of ['comparison', 'active', 'long']) {
    await open(page, scenario);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
    const primary = scenario === 'comparison' ? page.getByRole('button', { name: 'Démarrer ce plan' }) : page.getByRole('button', { name: 'J’ai effectué cette étape' });
    await primary.scrollIntoViewIfNeeded(); await expect(primary).toBeInViewport();
  }
});
