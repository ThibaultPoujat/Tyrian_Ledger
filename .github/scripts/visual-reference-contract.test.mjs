import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const root = new URL('../../docs/ux/', import.meta.url);
const directory = new URL('prototypes/2026-09-28/', root);
const screenIds = ['signaux', 'plan-comparison', 'active-plan', 'session-preferences', 'bilan', 'settings'];

test('all six approved screen references are intact original PNGs with accurate dimensions', async () => {
  const manifest = JSON.parse(await readFile(new URL('manifest.json', directory), 'utf8'));
  assert.equal(manifest.status, 'owner-approved');
  assert.deepEqual(manifest.targetViewport, { width: 1920, height: 1080 });
  assert.deepEqual(manifest.screens.map(screen => screen.id), screenIds);
  assert.equal(new Set(manifest.screens.map(screen => screen.file)).size, 6);
  for (const screen of manifest.screens) {
    assert.match(screen.file, /^\d{2}-[a-z-]+\.png$/);
    const bytes = await readFile(new URL(screen.file, directory));
    assert.equal(bytes.subarray(0, 8).toString('hex'), '89504e470d0a1a0a', screen.id);
    assert.equal(bytes.subarray(12, 16).toString(), 'IHDR', screen.id);
    assert.equal(bytes.readUInt32BE(16), screen.width, screen.id);
    assert.equal(bytes.readUInt32BE(20), screen.height, screen.id);
    assert.equal(createHash('sha256').update(bytes).digest('hex'), screen.sha256, screen.id);
  }
});

test('the visual authority links every screen and is reachable from agent and reviewer entry points', async () => {
  const manifest = JSON.parse(await readFile(new URL('manifest.json', directory), 'utf8'));
  const authority = await readFile(new URL(manifest.semantics, directory), 'utf8');
  for (const screen of manifest.screens) {
    assert.ok(authority.includes(`prototypes/2026-09-28/${screen.file}`), screen.id);
  }
  for (const path of ['AGENTS.md', '.codex/skills/tyrian-pr-review/SKILL.md', '.github/pull_request_template.md']) {
    const content = await readFile(new URL(`../../${path}`, import.meta.url), 'utf8');
    assert.ok(content.includes('docs/ux/tyrian-ledger-visual-reference.md'), path);
  }
});
