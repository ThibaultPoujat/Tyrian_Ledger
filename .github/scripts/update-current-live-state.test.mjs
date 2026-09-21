import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import {
  deriveLiveState,
  parseNonBlockingAlternate,
  parseSolGates,
  readTicketMap,
  renderGeneratedBlock,
  replaceGeneratedBlock,
} from './update-current-live-state.mjs';

const repositoryRoot = new URL('../../', import.meta.url);
const fixtureUrl = new URL('../fixtures/live-state-after-138.json', import.meta.url);

async function sourceFixture() {
  const [index, guide, fixture, ticketByIssue] = await Promise.all([
    readFile(new URL('docs/milestones/INDEX.md', repositoryRoot), 'utf8'),
    readFile(new URL('docs/workflow/model-effort-guide.md', repositoryRoot), 'utf8'),
    readFile(fixtureUrl, 'utf8'),
    readTicketMap(new URL('docs/milestones/', repositoryRoot).pathname),
  ]);
  return { index, guide, operational: JSON.parse(fixture), ticketByIssue };
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

function begin() { return '<!-- BEGIN GENERATED LIVE STATE -->'; }
function end() { return '<!-- END GENERATED LIVE STATE -->'; }
