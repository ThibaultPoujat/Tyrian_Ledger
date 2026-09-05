import assert from 'node:assert/strict';
import { access, readdir, readFile } from 'node:fs/promises';
import test from 'node:test';

const workflowDirectory = new URL('../workflows/', import.meta.url);

async function workflow(name) {
  return readFile(new URL(name, workflowDirectory), 'utf8');
}

test('every GitHub Action is pinned to an immutable full commit SHA', async () => {
  const workflowNames = (await readdir(workflowDirectory)).filter((name) => name.endsWith('.yml'));
  for (const name of workflowNames) {
    const content = await workflow(name);
    const uses = [...content.matchAll(/^\s*uses:\s*([^\s#]+)(?:\s+#.*)?$/gm)].map((match) => match[1]);
    assert.notEqual(uses.length, 0, `${name} must use at least one pinned action.`);
    for (const action of uses) {
      assert.match(action, /^[^@\s]+@[0-9a-f]{40}$/, `${name} has a mutable action reference: ${action}`);
    }
  }
});

test('active CI retains the required generic validation jobs and no Pages runtime', async () => {
  const content = await workflow('ci.yml');

  for (const job of ['secrets', 'backend', 'frontend', 'browser', 'workflow-contracts']) {
    assert.match(content, new RegExp(`^  ${job}:`, 'm'), `CI must retain the ${job} job.`);
  }
  assert.match(content, /node --test \.github\/scripts\/\*\.test\.mjs/);
  assert.doesNotMatch(content, /(?:pages-scheduler|pages:|id-token:\s*write|deploy-pages|pages-snapshot-scheduler|MarketSnapshotGenerator)/i);
});

test('the retired Pages workflow and selector are absent', async () => {
  await assert.rejects(access(new URL('../workflows/pages.yml', import.meta.url)));
  await assert.rejects(access(new URL('../pages-preview-selector.json', import.meta.url)));
});
