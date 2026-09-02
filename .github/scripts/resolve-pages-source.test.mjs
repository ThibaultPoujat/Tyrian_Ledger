import assert from 'node:assert/strict';
import test from 'node:test';
import {
  createGitHubPullRequestLookup,
  parsePreviewSelector,
  resolvePagesSource,
} from './resolve-pages-source.mjs';

const developSha = 'a'.repeat(40);
const selectedSha = 'b'.repeat(40);
const repository = 'ThibaultPoujat/Tyrian_Ledger';

function selector(selection) {
  return JSON.stringify({ schemaVersion: 1, selection });
}

function eligiblePullRequest(overrides = {}) {
  return {
    base: { ref: 'develop' },
    head: { repo: { full_name: repository }, sha: selectedSha },
    merged_at: null,
    state: 'open',
    ...overrides,
  };
}

async function resolve(configText, response = eligiblePullRequest()) {
  return resolvePagesSource({
    baseBranch: 'develop',
    configText,
    currentDevelopSha: developSha,
    getPullRequest: async () => response,
    repository,
  });
}

test('accepts only an exact immutable SHA and same-repository open develop pull request', async () => {
  const result = await resolve(selector({ headSha: selectedSha, pullRequestNumber: 42 }));

  assert.deepEqual(result, {
    kind: 'selected-open-pull-request',
    pullRequestNumber: 42,
    sourceSha: selectedSha,
  });
});

test('uses current develop for absent, disabled, malformed, or mutable selector data', async () => {
  const configs = [
    undefined,
    selector(null),
    '{not-json',
    JSON.stringify({ schemaVersion: 1, selection: { headSha: 'feature-branch', pullRequestNumber: 42 } }),
    JSON.stringify({ schemaVersion: 1, selection: { headSha: selectedSha.toUpperCase(), pullRequestNumber: 42 } }),
    JSON.stringify({ schemaVersion: 1, selection: { branch: 'feature-branch', headSha: selectedSha, pullRequestNumber: 42 } }),
    JSON.stringify({ schemaVersion: 2, selection: null }),
  ];

  for (const configText of configs) {
    const result = await resolve(configText);
    assert.equal(result.kind, 'develop-fallback');
    assert.equal(result.sourceSha, developSha);
  }
});

test('uses current develop when the selected pull request is closed, merged, cross-repository, on another base, or has another head', async () => {
  const configText = selector({ headSha: selectedSha, pullRequestNumber: 42 });
  const responses = [
    eligiblePullRequest({ state: 'closed' }),
    eligiblePullRequest({ merged_at: '2026-09-02T12:00:00Z', state: 'closed' }),
    eligiblePullRequest({ head: { repo: { full_name: 'fork-owner/Tyrian_Ledger' }, sha: selectedSha } }),
    eligiblePullRequest({ base: { ref: 'main' } }),
    eligiblePullRequest({ head: { repo: { full_name: repository }, sha: developSha } }),
  ];

  for (const response of responses) {
    const result = await resolve(configText, response);
    assert.equal(result.kind, 'develop-fallback');
    assert.equal(result.sourceSha, developSha);
  }
});

test('fails closed when GitHub pull-request lookup cannot be completed', async () => {
  const result = await resolvePagesSource({
    baseBranch: 'develop',
    configText: selector({ headSha: selectedSha, pullRequestNumber: 42 }),
    currentDevelopSha: developSha,
    getPullRequest: async () => { throw new Error('offline'); },
    repository,
  });

  assert.equal(result.kind, 'develop-fallback');
  assert.equal(result.sourceSha, developSha);
});

test('isolates the GitHub REST request behind a mocked lookup boundary', async () => {
  const calls = [];
  const lookup = createGitHubPullRequestLookup({
    apiUrl: 'https://api.github.test',
    fetchImpl: async (url, options) => {
      calls.push({ options, url });
      return { json: async () => eligiblePullRequest(), ok: true, status: 200 };
    },
    repository,
    token: 'workflow-token',
  });

  assert.deepEqual(await lookup(42), eligiblePullRequest());
  assert.equal(calls[0].url, `https://api.github.test/repos/${repository}/pulls/42`);
  assert.equal(calls[0].options.headers.authorization, 'Bearer workflow-token');
});

test('selector parser does not treat arbitrary objects as selections', () => {
  assert.deepEqual(parsePreviewSelector('[]'), { kind: 'fallback', reason: 'malformed-selector' });
  assert.deepEqual(parsePreviewSelector(selector({ headSha: selectedSha, pullRequestNumber: 0 })), { kind: 'fallback', reason: 'malformed-selection' });
});
