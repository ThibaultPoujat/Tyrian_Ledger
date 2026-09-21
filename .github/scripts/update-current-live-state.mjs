import { readFile, readdir, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, '..', '..');
const beginMarker = '<!-- BEGIN GENERATED LIVE STATE -->';
const endMarker = '<!-- END GENERATED LIVE STATE -->';

export function parseExecutionOrder(source, sourceName) {
  const section = source.match(/^## Current (?:explicit )?execution order\s*$([\s\S]*?)(?=^##\s|(?![\s\S]))/m)?.[1];
  if (!section) throw new Error(`${sourceName} has no current execution-order section.`);

  const explicitSequence = section.match(/```(?:text)?\s*([\s\S]*?)```/i)?.[1] ?? section;
  const order = [...explicitSequence.matchAll(/#(\d+)\b/g)].map((match) => Number(match[1]));
  if (order.length === 0) throw new Error(`${sourceName} has no issue references in its execution-order section.`);
  return [...new Set(order)];
}

export function parseNonBlockingAlternate(source) {
  const normalized = source.replace(/\s+/g, ' ');
  const preferredMatch = normalized.match(/#(\d+) is preferred before #(\d+)/i);
  const startMatch = normalized.match(/after #(\d+) merges[^.]*?#(\d+) may start/i);
  if (!preferredMatch || !startMatch) throw new Error('The roadmap has no parseable non-blocking alternate rule.');
  const preferred = Number(preferredMatch[1]);
  const alternate = Number(preferredMatch[2]);
  const prerequisite = Number(startMatch[1]);
  const repeatedAlternate = Number(startMatch[2]);
  if (alternate !== repeatedAlternate) throw new Error('The roadmap alternate rule is internally inconsistent.');
  return { preferred, alternate, prerequisite };
}

export function parseSolGates(guide) {
  const section = guide.match(/^## Separate Sol review gate\s*$([\s\S]*?)(?=^##\s|(?![\s\S]))/m)?.[1];
  if (!section) throw new Error('The model-effort guide has no separate Sol review-gate section.');

  const gates = [...section.matchAll(/^- #(\d+) \/ (TKT-[A-Z0-9-]+)/gm)]
    .map((match) => ({ number: Number(match[1]), ticket: match[2] }));
  if (gates.length === 0) throw new Error('The model-effort guide has no parseable active Sol gates.');
  return gates;
}

export function replaceGeneratedBlock(current, generatedBlock) {
  const beginCount = current.split(beginMarker).length - 1;
  const endCount = current.split(endMarker).length - 1;
  if (beginCount !== 1 || endCount !== 1) {
    throw new Error('CURRENT.md must contain exactly one generated live-state block.');
  }

  const pattern = new RegExp(`${escapeRegExp(beginMarker)}[\\s\\S]*?${escapeRegExp(endMarker)}`);
  return current.replace(pattern, `${beginMarker}\n${generatedBlock}\n${endMarker}`);
}

export function deriveLiveState({ index, issue98, guide, ticketByIssue, operational }) {
  const indexOrder = parseExecutionOrder(index, 'docs/milestones/INDEX.md');
  const issueOrder = parseExecutionOrder(issue98, 'issue #98');
  const allReferencedIssues = [...new Set([...indexOrder, ...issueOrder])];
  requireIssues(operational.issues, allReferencedIssues);

  if (indexOrder.join(',') !== issueOrder.join(',')) {
    throw new Error('Issue #98 and docs/milestones/INDEX.md disagree about the execution order.');
  }

  const indexOpenOrder = indexOrder.filter((number) => operational.issues[number].state === 'OPEN');

  const preferredNumber = indexOpenOrder[0];
  if (!preferredNumber) throw new Error('The roadmap has no open preferred next ticket.');
  const alternateRule = parseNonBlockingAlternate(index);
  const issueAlternateRule = parseNonBlockingAlternate(issue98);
  if (alternateRule.preferred !== issueAlternateRule.preferred
    || alternateRule.alternate !== issueAlternateRule.alternate
    || alternateRule.prerequisite !== issueAlternateRule.prerequisite) {
    throw new Error('Issue #98 and docs/milestones/INDEX.md disagree about the non-blocking alternate rule.');
  }
  requireIssues(operational.issues, [alternateRule.preferred, alternateRule.alternate, alternateRule.prerequisite]);
  const alternateNumber = operational.issues[alternateRule.preferred].state === 'OPEN'
    && operational.issues[alternateRule.alternate].state === 'OPEN'
    && operational.issues[alternateRule.prerequisite].state === 'CLOSED'
    ? alternateRule.alternate
    : null;

  const gates = parseSolGates(guide);
  for (const gate of gates) {
    if (ticketByIssue.get(gate.number) !== gate.ticket) {
      throw new Error(`The Sol-gate ticket mapping for #${gate.number} does not match its ticket contract.`);
    }
  }

  const completed = latestCompletedImplementation(operational.pullRequests, ticketByIssue);
  if (!completed) throw new Error('GitHub returned no merged implementation PR with a closed implementation issue.');
  const milestone = operational.issues[preferredNumber].milestoneTitle;
  if (!milestone) throw new Error(`The preferred issue #${preferredNumber} has no GitHub milestone.`);

  return {
    ticketByIssue,
    completed,
    milestone,
    preferred: ticketReference(preferredNumber, ticketByIssue),
    alternate: alternateNumber === null ? 'None' : ticketReference(alternateNumber, ticketByIssue),
    gates: gates.map((gate) => ticketReference(gate.number, ticketByIssue)),
  };
}

export function renderGeneratedBlock(liveState) {
  return [
    `- Last completed implementation ticket: \`${ticketReference(liveState.completed.issue.number, liveState.ticketByIssue)}\``,
    `- Last merged implementation PR: \`#${liveState.completed.number}\``,
    `- Active milestone: \`${liveState.milestone}\``,
    `- Preferred next implementation ticket: \`${liveState.preferred}\``,
    `- Allowed non-blocking alternate: ${liveState.alternate === 'None' ? '`None`' : `\`${liveState.alternate}\``}`,
    `- Explicit active Sol gates: ${liveState.gates.map((gate) => `\`${gate}\``).join(', ')}`,
    '- Authorities: operational state = `GitHub`; execution order = `issue #98 + docs/milestones/INDEX.md`; review gates = `docs/workflow/model-effort-guide.md`',
  ].join('\n');
}

export async function readTicketMap(ticketsRoot = join(repositoryRoot, 'docs', 'milestones')) {
  const ticketByIssue = new Map();
  for (const path of await markdownFiles(ticketsRoot)) {
    const fileName = path.split('/').at(-1);
    const ticket = fileName.match(/^(TKT-[A-Z0-9-]+)\.md$/)?.[1];
    if (!ticket) continue;
    const content = await readFile(path, 'utf8');
    const issue = content.match(/^GitHub issue:\s*#(\d+)\s*$/m)?.[1];
    if (!issue) continue;
    const number = Number(issue);
    if (ticketByIssue.has(number)) throw new Error(`Issue #${number} is mapped by more than one ticket contract.`);
    ticketByIssue.set(number, ticket);
  }
  return ticketByIssue;
}

export async function updateCurrentLiveState({ root = repositoryRoot, operational }) {
  const [current, index, guide, ticketByIssue] = await Promise.all([
    readFile(join(root, 'CURRENT.md'), 'utf8'),
    readFile(join(root, 'docs', 'milestones', 'INDEX.md'), 'utf8'),
    readFile(join(root, 'docs', 'workflow', 'model-effort-guide.md'), 'utf8'),
    readTicketMap(join(root, 'docs', 'milestones')),
  ]);
  const state = deriveLiveState({ index, issue98: operational.issue98Body, guide, ticketByIssue, operational });
  const updated = replaceGeneratedBlock(current, renderGeneratedBlock(state));
  if (updated !== current) await writeFile(join(root, 'CURRENT.md'), updated);
  return { changed: updated !== current, state };
}

async function fetchOperationalState() {
  const token = process.env.GITHUB_TOKEN;
  const repository = process.env.GITHUB_REPOSITORY;
  if (!token || !repository) throw new Error('GITHUB_TOKEN and GITHUB_REPOSITORY are required for the post-merge update.');
  const [owner, name] = repository.split('/');
  if (!owner || !name || repository.split('/').length !== 2) throw new Error('GITHUB_REPOSITORY must be owner/name.');

  const roadmapIndex = await readFile(join(repositoryRoot, 'docs', 'milestones', 'INDEX.md'), 'utf8');
  const referencedNumbers = [...new Set([...parseExecutionOrder(roadmapIndex, 'docs/milestones/INDEX.md'), 98])];
  const issueSelections = referencedNumbers
    .filter((number) => number !== 98)
    .map((number) => `issue${number}: issue(number: ${number}) { number state title milestone { title } }`)
    .join('\n');
  const query = `query($owner: String!, $name: String!) {
    repository(owner: $owner, name: $name) {
      issue98: issue(number: 98) { body }
      pullRequests(first: 100, states: MERGED, orderBy: { field: UPDATED_AT, direction: DESC }) {
        nodes {
          number
          mergedAt
          baseRefName
          closingIssuesReferences(first: 20) { nodes { number state title milestone { title } } }
        }
      }
      ${issueSelections}
    }
  }`;
  const response = await fetch('https://api.github.com/graphql', {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', 'User-Agent': 'tyrian-ledger-live-state-updater' },
    body: JSON.stringify({ query, variables: { owner, name } }),
  });
  if (!response.ok) throw new Error(`GitHub GraphQL returned HTTP ${response.status}.`);
  const payload = await response.json();
  if (payload.errors?.length || !payload.data?.repository?.issue98?.body) throw new Error('GitHub GraphQL returned incomplete live-state data.');

  const repositoryData = payload.data.repository;
  const issues = Object.fromEntries(referencedNumbers.filter((number) => number !== 98).map((number) => {
    const issue = repositoryData[`issue${number}`];
    if (!issue) throw new Error(`GitHub did not return roadmap issue #${number}.`);
    return [number, { state: issue.state, title: issue.title, milestoneTitle: issue.milestone?.title ?? null }];
  }));
  return {
    issue98Body: repositoryData.issue98.body,
    issues,
    pullRequests: repositoryData.pullRequests.nodes.map((pullRequest) => ({
      ...pullRequest,
      closingIssues: pullRequest.closingIssuesReferences.nodes.map((issue) => ({
        ...issue,
        milestoneTitle: issue.milestone?.title ?? null,
      })),
    })),
  };
}

function latestCompletedImplementation(pullRequests, ticketByIssue) {
  return pullRequests
    .filter((pullRequest) => pullRequest.baseRefName === 'develop' && pullRequest.mergedAt)
    .sort((left, right) => Date.parse(right.mergedAt) - Date.parse(left.mergedAt))
    .map((pullRequest) => ({
      ...pullRequest,
      issue: pullRequest.closingIssues.find((issue) => issue.state === 'CLOSED' && ticketByIssue.has(issue.number)),
    }))
    .find((pullRequest) => pullRequest.issue);
}

function ticketReference(number, ticketByIssue) {
  const ticket = ticketByIssue.get(number);
  if (!ticket) throw new Error(`Issue #${number} has no mapped ticket contract.`);
  return `${ticket} / #${number}`;
}

function requireIssues(issues, numbers) {
  for (const number of numbers) {
    if (!issues[number]) throw new Error(`GitHub state is missing issue #${number}.`);
  }
}

async function markdownFiles(root) {
  const entries = await readdir(root, { withFileTypes: true });
  const paths = await Promise.all(entries.map(async (entry) => {
    const path = join(root, entry.name);
    if (entry.isDirectory()) return markdownFiles(path);
    return entry.isFile() && entry.name.endsWith('.md') ? [path] : [];
  }));
  return paths.flat();
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

async function main() {
  const fixtureIndex = process.argv.indexOf('--fixture');
  const fixturePath = fixtureIndex >= 0 ? process.argv[fixtureIndex + 1] : null;
  if (fixtureIndex >= 0 && !fixturePath) throw new Error('--fixture requires a path.');
  const operational = fixturePath
    ? JSON.parse(await readFile(resolve(process.cwd(), fixturePath), 'utf8'))
    : await fetchOperationalState();
  const result = await updateCurrentLiveState({ operational });
  process.stdout.write(result.changed ? 'Updated CURRENT.md generated live state.\n' : 'CURRENT.md generated live state is already current.\n');
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main().catch((error) => {
    process.stderr.write(`Live-state update failed: ${error.message}\n`);
    process.exitCode = 1;
  });
}
