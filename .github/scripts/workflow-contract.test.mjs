import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import { join } from 'node:path';
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

test('the Pages publication workflow is develop-only, scheduled every 15 minutes away from the hour, and never uses pull_request_target', async () => {
  const content = await workflow('pages.yml');
  assert.match(content, /branches:\s*\[develop\]/);
  assert.match(content, /cron:\s*'7,22,37,52 \* \* \* \*'/);
  assert.match(content, /if:\s*github\.ref == 'refs\/heads\/develop'/);
  assert.doesNotMatch(content, /pull_request(?:_target)?:/);
  assert.match(content, /permissions:\s*\{\}/);
  assert.match(content, /cancel-in-progress:\s*true/);
});

test('only the deployment job holds Pages or OIDC permission, and it uses trusted develop scripts', async () => {
  const content = await workflow('pages.yml');
  assert.match(content, /resolve-source:[\s\S]*?pull-requests:\s*read/);
  assert.match(content, /build-static-candidate:[\s\S]*?permissions:\n\s+contents:\s*read/);
  assert.match(content, /audit-static-candidate:[\s\S]*?ref:\s*\$\{\{ github\.sha \}\}/);
  assert.match(content, /deploy-pages:[\s\S]*?id-token:\s*write[\s\S]*?pages:\s*write/);
  assert.match(content, /deploy-pages:[\s\S]*?audit-pages-artifact\.mjs/);
  assert.doesNotMatch(content.match(/build-static-candidate:[\s\S]*?audit-static-candidate:/)?.[0] ?? '', /pages:\s*write|id-token:\s*write/);
});
