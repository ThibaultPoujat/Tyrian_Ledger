import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import {
  deriveLiveState,
  parseNonBlockingAlternate,
  parseSolGates,
  readTicketContracts,
  readTicketMap,
  renderGeneratedBlock,
  replaceGeneratedBlock,
} from './update-current-live-state.mjs';

const repositoryRoot = new URL('../../', import.meta.url);
const fixtureUrl = new URL('../fixtures/live-state-after-138.json', import.meta.url);

async function sourceFixture() {
  const [fixture, ticketByIssue] = await Promise.all([
    readFile(fixtureUrl, 'utf8'),
    readTicketMap(new URL('docs/milestones/', repositoryRoot).pathname),
  ]);
  // Historical regression cases must retain their historical authorities when
  // the active roadmap and review gates legitimately change.
  const operational = JSON.parse(fixture);
  return { index: operational.roadmapIndex, guide: operational.modelGuide, operational, ticketByIssue };
}

async function liveState(overrides = {}) {
  const source = await sourceFixture();
  const operational = structuredClone(source.operational);
  Object.assign(operational, overrides);
  return deriveLiveState({
    index: source.index,
    issue98: operational.issue98Body,
    guide: source.guide,
    ticketByIssue: source.ticketByIssue,
    operational,
  });
}

test('merged implementation progression repairs the stale generated block from authoritative sources', async () => {
  const state = await liveState();
  const staleCurrent = [
    'Durable narrative stays intact.',
    '<!-- BEGIN GENERATED LIVE STATE -->',
    '- Last merged implementation PR: `#136`',
    '<!-- END GENERATED LIVE STATE -->',
  ].join('\n');
  const repaired = replaceGeneratedBlock(staleCurrent, renderGeneratedBlock(state));

  assert.match(repaired, /Last completed implementation ticket: `TKT-M21-S03 \/ #131`/);
  assert.match(repaired, /Last merged implementation PR: `#138`/);
  assert.match(repaired, /Preferred next implementation ticket: `TKT-M21-S01 \/ #129`/);
  assert.match(repaired, /Allowed non-blocking alternate: `None`/);
  assert.match(repaired, /Explicit active Sol gates: `TKT-M21-S05 \/ #133`, `TKT-M21-02 \/ #93`, `TKT-M21-03 \/ #94`, `TKT-M22-02 \/ #96`/);
});

test('lower-numbered open crafting work cannot override the explicit roadmap order', async () => {
  const state = await liveState();
  assert.equal(state.preferred, 'TKT-M21-S01 / #129');
  assert.equal(state.milestone, 'M21 — Signals and Crafting Intelligence');
  assert.notEqual(state.preferred, 'TKT-M21-02 / #93');
});

test('the documented #129/#130 exception creates only the allowed non-blocking alternate', async () => {
  const source = await sourceFixture();
  const operational = structuredClone(source.operational);
  operational.issues[130].state = 'OPEN';
  operational.issues[131].state = 'OPEN';
  const state = deriveLiveState({
    index: source.index,
    issue98: operational.issue98Body,
    guide: source.guide,
    ticketByIssue: source.ticketByIssue,
    operational,
  });

  assert.equal(state.preferred, 'TKT-M21-S01 / #129');
  assert.equal(state.alternate, 'TKT-M21-S02 / #130');
});

test('the non-blocking MVP route advances to #131 after #130 closes', async () => {
  const source = await sourceFixture();
  const operational = structuredClone(source.operational);
  operational.issues[130].state = 'CLOSED';
  operational.issues[131].state = 'OPEN';
  const state = deriveLiveState({
    index: source.index,
    issue98: operational.issue98Body,
    guide: source.guide,
    ticketByIssue: source.ticketByIssue,
    operational,
  });

  assert.equal(state.preferred, 'TKT-M21-S01 / #129');
  assert.equal(state.alternate, 'TKT-M21-S03 / #131');
});

test('the roadmap wording remains a parseable non-blocking alternate rule', async () => {
  const index = await readFile(new URL('docs/milestones/INDEX.md', repositoryRoot), 'utf8');
  assert.deepEqual(parseNonBlockingAlternate(index), {
    preferred: 129,
    alternate: 130,
    continuation: 131,
    prerequisite: 128,
  });
});

test('a disagreement about the documented alternate rule fails without updating handoff state', async () => {
  const source = await sourceFixture();
  const operational = structuredClone(source.operational);
  operational.issue98Body = operational.issue98Body.replace(
    '**MVP non-blocking exception:** #129 is preferred before #130. After #128 merges, #130 may start before #129. #131 depends on #130, not on completion of #129.',
    '**MVP non-blocking exception:** #129 is preferred before #132. After #128 merges, #132 may start before #129. #131 depends on #132, not on completion of #129.',
  );

  assert.throws(() => deriveLiveState({
    index: source.index,
    issue98: operational.issue98Body,
    guide: source.guide,
    ticketByIssue: source.ticketByIssue,
    operational,
  }), /disagree about the non-blocking alternate rule/);
});

test('a disagreement about a closed-ticket position in the execution order fails safely', async () => {
  const source = await sourceFixture();
  const operational = structuredClone(source.operational);
  operational.issue98Body = operational.issue98Body.replace(
    '3. #130 — spike\n4. #131 — MVP',
    '3. #131 — MVP\n4. #130 — spike',
  );

  assert.throws(() => deriveLiveState({
    index: source.index,
    issue98: operational.issue98Body,
    guide: source.guide,
    ticketByIssue: source.ticketByIssue,
    operational,
  }), /disagree about the execution order/);
});

test('Sol gates come only from the model-effort guide', async () => {
  const source = await sourceFixture();
  assert.deepEqual(parseSolGates(source.guide), [
    { number: 133, ticket: 'TKT-M21-S05' },
    { number: 93, ticket: 'TKT-M21-02' },
    { number: 94, ticket: 'TKT-M21-03' },
    { number: 96, ticket: 'TKT-M22-02' },
  ]);
});

test('an empty future Sol-gate list is rendered as None', async () => {
  const source = await sourceFixture();
  const guide = source.guide.replace(/^- #(?:133|93|94|96) \/ TKT-[A-Z0-9-]+.*\n/gm, '');
  const state = deriveLiveState({
    index: source.index,
    issue98: source.operational.issue98Body,
    guide,
    ticketByIssue: source.ticketByIssue,
    operational: source.operational,
  });

  assert.match(renderGeneratedBlock(state), /Explicit active Sol gates: `None`/);
});

test('the generated-state writer cannot modify durable CURRENT.md prose', () => {
  const durable = 'Durable context must survive unchanged.\n';
  const current = `${durable}${begin()}\nold generated value\n${end()}\n`;
  const updated = replaceGeneratedBlock(current, '- New generated value');

  assert.ok(updated.startsWith(durable));
  assert.match(updated, /- New generated value/);
  assert.throws(() => replaceGeneratedBlock('no markers', '- replacement'), /exactly one generated live-state block/);
  assert.throws(() => replaceGeneratedBlock(`${end()}\n${begin()}`, '- replacement'), /exactly one generated live-state block/);
});

test('the corrective roadmap stops coding at the explicit integration checkpoint', async () => {
  const [fixture, { ticketByIssue, checkpointIssues }] = await Promise.all([
    readFile(new URL('../fixtures/live-state-corrective-preparation.json', import.meta.url), 'utf8'),
    readTicketContracts(new URL('docs/milestones/', repositoryRoot).pathname),
  ]);
  const operational = JSON.parse(fixture);
  const { roadmapIndex: index, modelGuide: guide } = operational;
  function current() {
    return deriveLiveState({ index, issue98: operational.issue98Body, guide, ticketByIssue, checkpointIssues, operational });
  }
  assert.equal(current().preferred, 'TKT-M22-P00 / #148');
  assert.equal(current().preferredKind, 'implementation');
  assert.equal(current().alternate, 'None');
  assert.equal(current().completed.number, 146);
  assert.deepEqual(current().gates, ['TKT-M22-P01A / #149', 'TKT-M22-02 / #96']);
  operational.issues[148].state = 'CLOSED';
  assert.equal(current().preferred, 'TKT-M22-P01A / #149');
  operational.issues[149].state = 'CLOSED';
  assert.equal(current().preferred, 'TKT-M22-G01 / #150');
  assert.equal(current().preferredKind, 'checkpoint');
  const block = renderGeneratedBlock(current());
  assert.match(block, /Preferred next implementation ticket: `None — planning checkpoint required`/);
  assert.match(block, /Next required checkpoint: `TKT-M22-G01 \/ #150`/);
  assert.doesNotMatch(block, /Preferred next implementation ticket: `TKT-/);
  operational.issues[150].state = 'CLOSED';
  assert.equal(current().preferred, 'TKT-M22-02 / #96');
  assert.equal(current().preferredKind, 'implementation');
});

test('P01A checkpoint prepares only P01B before returning to the integration gate', async () => {
  const [fixture, { ticketByIssue, checkpointIssues }] = await Promise.all([
    readFile(new URL('../fixtures/live-state-p01a-checkpoint.json', import.meta.url), 'utf8'),
    readTicketContracts(new URL('docs/milestones/', repositoryRoot).pathname),
  ]);
  const operational = JSON.parse(fixture);
  const { roadmapIndex: index, modelGuide: guide } = operational;
  const current = () => deriveLiveState({ index, issue98: operational.issue98Body, guide, ticketByIssue, checkpointIssues, operational });
  assert.equal(current().completed.number, 152);
  assert.equal(current().preferred, 'TKT-M22-C01 / #153');
  assert.equal(current().alternate, 'None');
  assert.deepEqual(current().gates, ['TKT-M22-P01B / #154', 'TKT-M22-02 / #96']);
  operational.issues[153].state = 'CLOSED';
  assert.equal(current().preferred, 'TKT-M22-P01B / #154');
  assert.equal(current().preferredKind, 'implementation');
  operational.issues[154].state = 'CLOSED';
  assert.equal(current().preferred, 'TKT-M22-G01 / #150');
  assert.match(renderGeneratedBlock(current()), /Preferred next implementation ticket: `None — planning checkpoint required`/);
  assert.match(renderGeneratedBlock(current()), /Next required checkpoint: `TKT-M22-G01 \/ #150`/);
});

async function batchFixture() {
  const [fixture, contracts] = await Promise.all([
    readFile(new URL('../fixtures/live-state-batch-01.json', import.meta.url), 'utf8'),
    readTicketContracts(new URL('docs/milestones/', repositoryRoot).pathname),
  ]);
  const operational = JSON.parse(fixture);
  return { index: operational.roadmapIndex, guide: operational.modelGuide,
    ...contracts, issue98: operational.issue98Body, operational };
}

test('the prepared batch advances across four tickets and stops at the integration checkpoint', async () => {
  const source = await batchFixture();
  const current = () => deriveLiveState(source);
  assert.equal(current().completed.number, 156);
  assert.equal(current().preferred, 'TKT-M22-C02 / #157');
  assert.deepEqual(current().gates, ['TKT-M22-P01C / #158', 'TKT-M22-02 / #96']);
  for (const [closed, next] of [
    [157, 'TKT-M22-P01C / #158'],
    [158, 'TKT-M22-P01D / #159'],
    [159, 'TKT-M22-P05A / #160'],
    [160, 'TKT-M22-P05B / #161'],
  ]) {
    source.operational.issues[closed].state = 'CLOSED';
    assert.equal(current().preferred, next);
    assert.equal(current().preferredKind, 'implementation');
    assert.equal(current().alternate, 'None');
  }
  // The model guide still lists #158; routine closure removes only its active status.
  assert.ok(parseSolGates(source.guide).some(gate => gate.number === 158));
  assert.deepEqual(current().gates, ['TKT-M22-02 / #96']);
  source.operational.issues[158].state = 'OPEN';
  assert.deepEqual(current().gates, ['TKT-M22-P01C / #158', 'TKT-M22-02 / #96']);
  source.operational.issues[158].state = 'CLOSED';
  source.operational.issues[161].state = 'CLOSED';
  assert.equal(current().preferred, 'TKT-M22-G01 / #150');
  assert.equal(current().preferredKind, 'checkpoint');
  assert.match(renderGeneratedBlock(current()), /Preferred next implementation ticket: `None — planning checkpoint required`/);
  assert.doesNotMatch(renderGeneratedBlock(current()), /Preferred next implementation ticket: `TKT-M22-02/);
});

test('batch queue disagreement fails closed rather than selecting another prepared ticket', async () => {
  const source = await batchFixture();
  source.issue98 = source.issue98.replace(' -> #159 TKT-M22-P01D', ' -> #161 TKT-M22-P05B');
  assert.throws(() => deriveLiveState(source), /disagree about the execution order/);
});

test('a listed review gate cannot disappear because GitHub authority is missing', async () => {
  const source = await batchFixture();
  // A valid mapped gate outside the current queue still requires operational state.
  source.guide = source.guide.replace('## Separate Sol review gate',
    '## Separate Sol review gate\n\n- #999 / TKT-M22-TEST — explicit additional gate for this failure fixture.');
  source.ticketByIssue.set(999, 'TKT-M22-TEST');
  assert.throws(() => deriveLiveState(source), /GitHub state is missing issue #999/);
});

async function secondBatchFixture() {
  const [index, guide, fixture, contracts] = await Promise.all([
    readFile(new URL('docs/milestones/INDEX.md', repositoryRoot), 'utf8'),
    readFile(new URL('docs/workflow/model-effort-guide.md', repositoryRoot), 'utf8'),
    readFile(new URL('../fixtures/live-state-batch-02.json', import.meta.url), 'utf8'),
    readTicketContracts(new URL('docs/milestones/', repositoryRoot).pathname),
  ]);
  const operational = JSON.parse(fixture);
  return { index, guide, ...contracts, issue98: operational.issue98Body, operational };
}

test('B2 selects preparation then four bounded contracts and stops before release work', async () => {
  const source = await secondBatchFixture();
  const current = () => deriveLiveState(source);
  assert.equal(current().completed.number, 166);
  assert.equal(current().preferred, 'TKT-M22-C03 / #167');
  assert.deepEqual(current().gates, ['TKT-M22-P03A / #170', 'TKT-M22-02 / #96']);
  const delivered = [
    [167, 'TKT-M22-P02A / #168'],
    [168, 'TKT-M22-P02B / #169'],
    [169, 'TKT-M22-P03A / #170'],
    [170, 'TKT-M22-P02C / #171'],
  ];
  for (const [closed, next] of delivered) {
    source.operational.issues[closed].state = 'CLOSED';
    assert.equal(current().preferred, next);
    assert.equal(current().preferredKind, 'implementation');
    assert.equal(current().alternate, 'None');
  }
  assert.deepEqual(current().gates, ['TKT-M22-02 / #96']);
  source.operational.issues[171].state = 'CLOSED';
  assert.equal(current().preferred, 'TKT-M22-G01 / #150');
  assert.equal(current().preferredKind, 'checkpoint');
  assert.match(renderGeneratedBlock(current()), /None — planning checkpoint required/);
  assert.doesNotMatch(renderGeneratedBlock(current()), /Preferred next implementation ticket: `TKT-M22-02/);
  // Closing an issue is not proof of a newly merged implementation; the latest
  // implementation field still comes from actual PR evidence, not these states.
  assert.equal(current().completed.number, 166);
});

test('B2 only advances completed delivery from an actual merged issue-closing PR', async () => {
  const source = await secondBatchFixture();
  source.operational.issues[167].state = 'CLOSED';
  const issue = source.operational.issues[167];
  source.operational.pullRequests.push({ number: 999, mergedAt: '2026-10-02T12:00:00Z',
    baseRefName: 'develop', closingIssues: [{ number: 167, ...issue }] });
  const state = deriveLiveState(source);
  assert.equal(state.completed.number, 999);
  assert.equal(state.preferred, 'TKT-M22-P02A / #168');
});

test('B2 source disagreement and missing explicit gate state fail closed', async () => {
  const disagreement = await secondBatchFixture();
  disagreement.issue98 = disagreement.issue98.replace(' -> #170 TKT-M22-P03A', ' -> #171 TKT-M22-P02C');
  assert.throws(() => deriveLiveState(disagreement), /disagree about the execution order/);
  const missing = await secondBatchFixture();
  delete missing.operational.issues[170];
  assert.throws(() => deriveLiveState(missing), /GitHub state is missing issue #170/);
});

function begin() { return '<!-- BEGIN GENERATED LIVE STATE -->'; }
function end() { return '<!-- END GENERATED LIVE STATE -->'; }
