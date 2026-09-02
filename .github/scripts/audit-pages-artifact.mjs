import { lstat, readdir, readFile } from 'node:fs/promises';
import { join, relative, sep } from 'node:path';

const SECRET_PATTERNS = [
  /github_pat_[A-Za-z0-9_]{20,}/,
  /gh[pousr]_[A-Za-z0-9]{20,}/,
  /(?:AKIA|ASIA)[A-Z0-9]{16}/,
  /-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----/,
];

const PROHIBITED_CONTENT_PATTERNS = [
  { label: 'a local /api request path', pattern: /(?:["'`])\/api(?:\/|["'`?])/i },
  { label: 'a Guild Wars 2 browser endpoint', pattern: /(?:api\.)?guildwars2\.com/i },
  { label: 'a local filesystem path', pattern: /(?:\/Users\/|\/home\/runner\/|[A-Za-z]:\\)/ },
];

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function isAllowedAssetPath(path) {
  if (!path.startsWith('assets/')) return false;
  return path.split('/').slice(1).every((segment) => /^[A-Za-z0-9][A-Za-z0-9._-]*$/.test(segment));
}

function assertExactKeys(value, keys, description) {
  assert(value !== null && typeof value === 'object' && !Array.isArray(value), `${description} must be an object.`);
  const actualKeys = Object.keys(value).sort();
  assert(actualKeys.length === keys.length && actualKeys.every((key, index) => key === keys[index]), `${description} contains an unexpected field.`);
}

function assertPositiveSafeInteger(value, description) {
  assert(Number.isSafeInteger(value) && value > 0, `${description} must be a positive safe integer.`);
}

export function validateSnapshotContract(payload) {
  assertExactKeys(payload, ['candidates', 'capturePolicy', 'compatibility', 'contractVersion', 'generatedAtUtc'], 'The market snapshot');
  assert(payload.contractVersion === 1, 'The market snapshot contract version must be 1.');
  assert(typeof payload.generatedAtUtc === 'string' && /Z$/.test(payload.generatedAtUtc) && Number.isFinite(Date.parse(payload.generatedAtUtc)), 'The market snapshot generation time must be UTC ISO-8601.');

  assertExactKeys(payload.compatibility, ['moneyUnit', 'normalStackLimit', 'recommendationPolicyVersion'], 'The compatibility block');
  assert(payload.compatibility.moneyUnit === 'copper', 'The market snapshot must use copper.');
  assert(payload.compatibility.recommendationPolicyVersion === 'm9-v1', 'The market snapshot policy version is not supported.');
  assert(payload.compatibility.normalStackLimit === 250, 'The market snapshot stack limit is not supported.');

  assertExactKeys(payload.capturePolicy, ['burstBudget', 'maxConcurrentRequests', 'requestsPerSecond'], 'The capture policy block');
  assert(payload.capturePolicy.requestsPerSecond === 2 && payload.capturePolicy.maxConcurrentRequests === 2 && payload.capturePolicy.burstBudget === 20, 'The market snapshot does not record the required M10 capture policy.');

  assert(Array.isArray(payload.candidates), 'The market snapshot candidates must be an array.');
  for (const candidate of payload.candidates) {
    assertExactKeys(candidate, ['buys', 'itemId', 'itemName', 'sells'], 'A market snapshot candidate');
    assertPositiveSafeInteger(candidate.itemId, 'A candidate item ID');
    assert(typeof candidate.itemName === 'string', 'A candidate item name must be a string.');
    for (const [side, levels] of [['buys', candidate.buys], ['sells', candidate.sells]]) {
      assert(Array.isArray(levels), `Candidate ${side} must be an array.`);
      for (const level of levels) {
        assertExactKeys(level, ['listingCount', 'quantity', 'unitPriceInCopper'], `A candidate ${side} level`);
        assertPositiveSafeInteger(level.listingCount, `A candidate ${side} listing count`);
        assertPositiveSafeInteger(level.quantity, `A candidate ${side} quantity`);
        assertPositiveSafeInteger(level.unitPriceInCopper, `A candidate ${side} unit price`);
      }
    }
  }
}

async function collectFiles(directory, rootDirectory, result) {
  const entries = await readdir(directory, { withFileTypes: true });
  for (const entry of entries) {
    const absolutePath = join(directory, entry.name);
    const information = await lstat(absolutePath);
    const artifactPath = relative(rootDirectory, absolutePath).split(sep).join('/');
    assert(!information.isSymbolicLink(), `The artifact must not contain symbolic links (${artifactPath}).`);

    if (information.isDirectory()) {
      await collectFiles(absolutePath, rootDirectory, result);
    } else {
      assert(information.isFile(), `The artifact must contain only files and directories (${artifactPath}).`);
      assert(information.nlink === 1, `The artifact must not contain hard-linked files (${artifactPath}).`);
      result.push({ artifactPath, absolutePath });
    }
  }
}

export async function auditPagesArtifact(artifactDirectory) {
  const rootInformation = await lstat(artifactDirectory);
  assert(rootInformation.isDirectory(), 'The Pages artifact directory is missing.');

  const files = [];
  await collectFiles(artifactDirectory, artifactDirectory, files);
  const paths = new Set(files.map(({ artifactPath }) => artifactPath));
  assert(paths.has('index.html'), 'The Pages artifact must contain index.html.');
  assert(paths.has('market-snapshot.json'), 'The Pages artifact must contain market-snapshot.json.');
  assert([...paths].some((path) => /^assets\/.+\.js$/.test(path)), 'The Pages artifact must contain a compiled React JavaScript asset.');

  for (const file of files) {
    assert(file.artifactPath === 'index.html' || file.artifactPath === 'market-snapshot.json' || isAllowedAssetPath(file.artifactPath), `The Pages artifact contains a non-static or unexpected path (${file.artifactPath}).`);
    const content = await readFile(file.absolutePath, 'utf8');
    assert(!SECRET_PATTERNS.some((pattern) => pattern.test(content)), `The Pages artifact contains a credential-shaped value (${file.artifactPath}).`);
    const prohibited = PROHIBITED_CONTENT_PATTERNS.find(({ pattern }) => pattern.test(content));
    assert(prohibited === undefined, `The Pages artifact contains ${prohibited?.label} (${file.artifactPath}).`);
  }

  let snapshot;
  try {
    snapshot = JSON.parse(await readFile(join(artifactDirectory, 'market-snapshot.json'), 'utf8'));
  } catch {
    throw new Error('The Pages artifact market snapshot is not valid JSON.');
  }
  validateSnapshotContract(snapshot);

  return { fileCount: files.length };
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const [artifactDirectory] = process.argv.slice(2);
  if (artifactDirectory === undefined || process.argv.length !== 3) {
    process.stderr.write('Usage: audit-pages-artifact.mjs <artifact-directory>\n');
    process.exitCode = 1;
  } else {
    auditPagesArtifact(artifactDirectory).then(
      ({ fileCount }) => process.stdout.write(`Static Pages artifact audit passed (${fileCount} files).\n`),
      (error) => {
        process.stderr.write(`${error instanceof Error ? error.message : 'Could not audit the Pages artifact.'}\n`);
        process.exitCode = 1;
      },
    );
  }
}
