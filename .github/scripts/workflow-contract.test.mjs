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

test('the Pages publication workflow is develop-only, scheduled every 15 minutes, and never uses pull_request_target', async () => {
  const content = await workflow('pages.yml');
  assert.match(content, /branches:\s*\[develop\]/);
  assert.match(content, /cron:\s*'0,15,30,45 \* \* \* \*'/);
  assert.match(content, /if:\s*github\.ref == 'refs\/heads\/develop'/);
  assert.doesNotMatch(content, /pull_request(?:_target)?:/);
  assert.match(content, /permissions:\s*\{\}/);
  assert.match(content, /cancel-in-progress:\s*true/);
});

test('the static base and revision are exported before the React build step', async () => {
  const content = await workflow('pages.yml');
  const configurationStart = content.indexOf('- name: Export static build configuration');
  const buildStart = content.indexOf('- name: Build React assets against this snapshot revision');
  const assemblyStart = content.indexOf('- name: Assemble static artifact');

  assert.ok(configurationStart >= 0, 'The Pages workflow must export static build configuration.');
  assert.ok(buildStart > configurationStart, 'The React build must run after static build configuration is exported.');
  assert.ok(assemblyStart > buildStart, 'The static artifact must be assembled after the React build.');

  const configuration = content.slice(configurationStart, buildStart);
  assert.match(configuration, /VITE_SITE_BASE_PATH=.*\$GITHUB_ENV/);
  assert.match(configuration, /VITE_MARKET_SNAPSHOT_PATH=.*\$GITHUB_ENV/);

  const build = content.slice(buildStart, assemblyStart);
  assert.match(build, /npm --prefix frontend run build/);
  assert.doesNotMatch(build, /\$GITHUB_ENV/, 'GitHub environment exports apply only to later steps.');
});

test('the Pages artifact and deployment actions use reviewed Node 24 releases', async () => {
  const content = await workflow('pages.yml');
  assert.match(content, /actions\/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a # v7\.0\.1/);
  assert.match(content, /actions\/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c # v8\.0\.1/);
  assert.match(content, /actions\/configure-pages@45bfe0192ca1faeb007ade9deae92b16b8254a0d # v6\.0\.0/);
  assert.match(content, /actions\/upload-pages-artifact@fc324d3547104276b827a68afc52ff2a11cc49c9 # v5\.0\.0/);
  assert.match(content, /actions\/deploy-pages@368f82528645a54fb793d4d04e342629a3f51346 # v5\.0\.1/);
});

test('only the deployment job holds Pages or OIDC permission, and it uses trusted develop scripts', async () => {
  const content = await workflow('pages.yml');
  assert.match(content, /resolve-source:[\s\S]*?pull-requests:\s*read/);
  assert.match(content, /build-static-candidate:[\s\S]*?permissions:\n\s+contents:\s*read/);
  assert.match(content, /audit-static-candidate:[\s\S]*?ref:\s*\$\{\{ github\.sha \}\}/);
  assert.match(content, /deploy-pages:[\s\S]*?id-token:\s*write[\s\S]*?pages:\s*write/);
  assert.match(content, /deploy-pages:[\s\S]*?audit-pages-artifact\.mjs/);

  const jobsContent = content.slice(content.indexOf('\njobs:\n') + '\njobs:\n'.length);
  const starts = [...jobsContent.matchAll(/^  ([a-z][a-z0-9-]*):\n/gm)];
  assert.notEqual(starts.length, 0, 'The Pages workflow must define jobs.');
  for (const [index, match] of starts.entries()) {
    const jobName = match[1];
    const jobStart = match.index ?? 0;
    const jobEnd = starts[index + 1]?.index ?? jobsContent.length;
    const job = jobsContent.slice(jobStart, jobEnd);
    if (jobName !== 'deploy-pages') {
      assert.doesNotMatch(job, /(?:id-token|pages):\s*write/, `${jobName} must not hold deployment permission.`);
    }
  }
});
