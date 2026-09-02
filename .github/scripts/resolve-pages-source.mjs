import { appendFile, readFile } from 'node:fs/promises';

export const IMMUTABLE_SHA_PATTERN = /^[0-9a-f]{40}$/;

function isPlainObject(value) {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function hasOnlyKeys(value, keys) {
  return Object.keys(value).every((key) => keys.includes(key));
}

function fallback(currentDevelopSha, reason) {
  return {
    kind: 'develop-fallback',
    reason,
    sourceSha: currentDevelopSha,
  };
}

/**
 * Parses the reviewed selector without accepting branch names, refs, or
 * additional unreviewed fields. A null selection intentionally means develop.
 */
export function parsePreviewSelector(configText) {
  if (configText === undefined) return { kind: 'fallback', reason: 'missing-selector' };

  let payload;
  try {
    payload = JSON.parse(configText);
  } catch {
    return { kind: 'fallback', reason: 'malformed-json' };
  }

  if (!isPlainObject(payload) || !hasOnlyKeys(payload, ['schemaVersion', 'selection']) || payload.schemaVersion !== 1) {
    return { kind: 'fallback', reason: 'malformed-selector' };
  }

  if (payload.selection === null) return { kind: 'fallback', reason: 'selection-disabled' };

  if (!isPlainObject(payload.selection) ||
    !hasOnlyKeys(payload.selection, ['pullRequestNumber', 'headSha']) ||
    !Number.isSafeInteger(payload.selection.pullRequestNumber) ||
    payload.selection.pullRequestNumber <= 0 ||
    typeof payload.selection.headSha !== 'string' ||
    !IMMUTABLE_SHA_PATTERN.test(payload.selection.headSha)) {
    return { kind: 'fallback', reason: 'malformed-selection' };
  }

  return {
    headSha: payload.selection.headSha,
    kind: 'selection',
    pullRequestNumber: payload.selection.pullRequestNumber,
  };
}

/**
 * Narrows the untyped GitHub REST response to the exact, same-repository open
 * pull-request head that the trusted workflow is allowed to publish.
 */
export function isEligibleOpenPullRequest(payload, repository, baseBranch, expectedHeadSha) {
  return isPlainObject(payload) &&
    payload.state === 'open' &&
    payload.merged_at === null &&
    isPlainObject(payload.base) &&
    payload.base.ref === baseBranch &&
    isPlainObject(payload.head) &&
    payload.head.sha === expectedHeadSha &&
    isPlainObject(payload.head.repo) &&
    payload.head.repo.full_name === repository;
}

export async function resolvePagesSource({
  baseBranch,
  configText,
  currentDevelopSha,
  getPullRequest,
  repository,
}) {
  if (!IMMUTABLE_SHA_PATTERN.test(currentDevelopSha)) {
    throw new Error('The current develop SHA must be a lowercase immutable Git SHA.');
  }

  const parsed = parsePreviewSelector(configText);
  if (parsed.kind !== 'selection') return fallback(currentDevelopSha, parsed.reason);

  try {
    const pullRequest = await getPullRequest(parsed.pullRequestNumber);
    if (!isEligibleOpenPullRequest(pullRequest, repository, baseBranch, parsed.headSha)) {
      return fallback(currentDevelopSha, 'ineligible-pull-request');
    }
  } catch {
    return fallback(currentDevelopSha, 'pull-request-lookup-failed');
  }

  return {
    kind: 'selected-open-pull-request',
    pullRequestNumber: parsed.pullRequestNumber,
    sourceSha: parsed.headSha,
  };
}

export function createGitHubPullRequestLookup({ apiUrl, fetchImpl = fetch, repository, token }) {
  if (typeof token !== 'string' || token.length === 0) {
    throw new Error('A GitHub token is required only to validate an enabled selector.');
  }

  return async (pullRequestNumber) => {
    const response = await fetchImpl(
      `${apiUrl}/repos/${repository}/pulls/${pullRequestNumber}`,
      {
        headers: {
          accept: 'application/vnd.github+json',
          authorization: `Bearer ${token}`,
          'x-github-api-version': '2022-11-28',
        },
      },
    );

    if (!response.ok) throw new Error(`GitHub pull-request lookup failed with HTTP ${response.status}.`);
    return response.json();
  };
}

function parseArguments(argumentsList) {
  const values = new Map();
  for (let index = 0; index < argumentsList.length; index += 2) {
    const name = argumentsList[index];
    const value = argumentsList[index + 1];
    if (!['--base-branch', '--config', '--develop-sha', '--output', '--repository'].includes(name) ||
      value === undefined || values.has(name)) {
      throw new Error('Usage: resolve-pages-source.mjs --config <path> --repository <owner/repo> --base-branch <branch> --develop-sha <sha> --output <github-output-path>');
    }
    values.set(name, value);
  }

  if (values.size !== 5) {
    throw new Error('Usage: resolve-pages-source.mjs --config <path> --repository <owner/repo> --base-branch <branch> --develop-sha <sha> --output <github-output-path>');
  }

  return Object.fromEntries(values);
}

async function readOptionalConfig(path) {
  try {
    return await readFile(path, 'utf8');
  } catch (error) {
    if (error && typeof error === 'object' && error.code === 'ENOENT') return undefined;
    throw error;
  }
}

async function main() {
  const argumentsObject = parseArguments(process.argv.slice(2));
  const configText = await readOptionalConfig(argumentsObject['--config']);
  const selection = parsePreviewSelector(configText);
  const getPullRequest = selection.kind === 'selection'
    ? createGitHubPullRequestLookup({
      apiUrl: process.env.GITHUB_API_URL ?? 'https://api.github.com',
      repository: argumentsObject['--repository'],
      token: process.env.GITHUB_TOKEN,
    })
    : async () => undefined;

  const resolution = await resolvePagesSource({
    baseBranch: argumentsObject['--base-branch'],
    configText,
    currentDevelopSha: argumentsObject['--develop-sha'],
    getPullRequest,
    repository: argumentsObject['--repository'],
  });

  await appendFile(
    argumentsObject['--output'],
    `source_sha=${resolution.sourceSha}\nsource_kind=${resolution.kind}\n`,
    'utf8',
  );
  process.stdout.write(`Resolved ${resolution.kind} source ${resolution.sourceSha}.\n`);
}

if (import.meta.url === `file://${process.argv[1]}`) {
  main().catch((error) => {
    process.stderr.write(`${error instanceof Error ? error.message : 'Could not resolve the Pages source.'}\n`);
    process.exitCode = 1;
  });
}
