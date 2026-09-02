import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { assemblePagesArtifact } from './assemble-pages-artifact.mjs';
import { auditPagesArtifact, validateSnapshotContract } from './audit-pages-artifact.mjs';

const snapshot = {
  candidates: [{
    buys: [{ listingCount: 3, quantity: 10, unitPriceInCopper: 1_000 }],
    itemId: 900001,
    itemName: 'Synthetic public item',
    sells: [{ listingCount: 3, quantity: 10, unitPriceInCopper: 1_500 }],
  }],
  capturePolicy: { burstBudget: 20, maxConcurrentRequests: 2, requestsPerSecond: 2 },
  compatibility: { moneyUnit: 'copper', normalStackLimit: 250, recommendationPolicyVersion: 'm9-v1' },
  contractVersion: 1,
  generatedAtUtc: '2026-09-02T12:00:00.0000000Z',
};

async function createFixture() {
  const root = await mkdtemp(join(tmpdir(), 'tyrian-ledger-pages-'));
  const dist = join(root, 'dist');
  await mkdir(join(dist, 'assets'), { recursive: true });
  await writeFile(join(dist, 'index.html'), '<!doctype html><script src="/Tyrian_Ledger/assets/app.js"></script>');
  await writeFile(join(dist, 'assets', 'app.js'), 'console.log("static only");');
  const sourceSnapshot = join(root, 'market-snapshot.json');
  await writeFile(sourceSnapshot, JSON.stringify(snapshot));
  return { artifact: join(root, 'artifact'), dist, root, sourceSnapshot };
}

test('assembles and audits exactly the static React assets and public snapshot', async () => {
  const fixture = await createFixture();
  try {
    await assemblePagesArtifact({
      distDirectory: fixture.dist,
      outputDirectory: fixture.artifact,
      snapshotPath: fixture.sourceSnapshot,
    });

    assert.equal(await readFile(join(fixture.artifact, 'market-snapshot.json'), 'utf8'), JSON.stringify(snapshot));
    await assert.doesNotReject(auditPagesArtifact(fixture.artifact));
  } finally {
    await rm(fixture.root, { force: true, recursive: true });
  }
});

test('rejects a browser /api path and an upstream endpoint', async () => {
  const contents = [
    'fetch("/api/recommendations")',
    'fetch("https://api.guildwars2.com/v2/commerce/prices")',
    'debug = "/Users/example/private"',
  ];

  for (const content of contents) {
    const fixture = await createFixture();
    try {
      await assemblePagesArtifact({ distDirectory: fixture.dist, outputDirectory: fixture.artifact, snapshotPath: fixture.sourceSnapshot });
      await writeFile(join(fixture.artifact, 'assets', 'app.js'), content);
      await assert.rejects(auditPagesArtifact(fixture.artifact));
    } finally {
      await rm(fixture.root, { force: true, recursive: true });
    }
  }
});

test('rejects unexpected artifact files', async () => {
  const fixture = await createFixture();
  try {
    await assemblePagesArtifact({ distDirectory: fixture.dist, outputDirectory: fixture.artifact, snapshotPath: fixture.sourceSnapshot });
    await writeFile(join(fixture.artifact, 'operator-notes.txt'), 'not deployable');
    await assert.rejects(auditPagesArtifact(fixture.artifact));
  } finally {
    await rm(fixture.root, { force: true, recursive: true });
  }
});

test('rejects secret-shaped content and unsupported snapshot fields', async () => {
  const fixture = await createFixture();
  try {
    await assemblePagesArtifact({ distDirectory: fixture.dist, outputDirectory: fixture.artifact, snapshotPath: fixture.sourceSnapshot });
    await writeFile(join(fixture.artifact, 'assets', 'app.js'), 'const token = "ghp_123456789012345678901234567890123456";');
    await assert.rejects(auditPagesArtifact(fixture.artifact));
    await assert.throws(() => validateSnapshotContract({ ...snapshot, accountId: 'not-allowed' }));
  } finally {
    await rm(fixture.root, { force: true, recursive: true });
  }
});

test('enforces every browser snapshot parser invariant before deployment', () => {
  const malformedTimestamps = [
    '2026-09-02T12:00:00Z',
    '2026-02-30T12:00:00.0000000Z',
  ];
  for (const generatedAtUtc of malformedTimestamps) {
    assert.throws(() => validateSnapshotContract({ ...snapshot, generatedAtUtc }));
  }

  assert.throws(() => validateSnapshotContract({
    ...snapshot,
    candidates: Array.from({ length: 201 }, (_, index) => ({
      ...snapshot.candidates[0],
      itemId: index + 1,
    })),
  }));
  assert.throws(() => validateSnapshotContract({
    ...snapshot,
    candidates: [snapshot.candidates[0], { ...snapshot.candidates[0], itemId: 900001 }],
  }));
  assert.throws(() => validateSnapshotContract({
    ...snapshot,
    candidates: [{ ...snapshot.candidates[0], itemName: '   '}],
  }));
  assert.throws(() => validateSnapshotContract({
    ...snapshot,
    candidates: [{ ...snapshot.candidates[0], buys: [] }],
  }));
  for (const unorderedLevel of [
    { listingCount: 3, quantity: 10, unitPriceInCopper: 1_499 },
    { listingCount: 3, quantity: 9, unitPriceInCopper: 1_500 },
    { listingCount: 2, quantity: 10, unitPriceInCopper: 1_500 },
  ]) {
    assert.throws(() => validateSnapshotContract({
      ...snapshot,
      candidates: [{
        ...snapshot.candidates[0],
        sells: [...snapshot.candidates[0].sells, unorderedLevel],
      }],
    }));
  }
});
