import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

import worker, {
  SchedulerFailure,
  createGitHubAppJwt,
  dispatchPagesWorkflow,
  runScheduledDispatch,
} from '../out/index.js';

const enabledEnvironment = Object.freeze({
  GITHUB_APP_ID: '123',
  GITHUB_APP_INSTALLATION_ID: '456',
  GITHUB_APP_PRIVATE_KEY: 'test-private-key-input',
  SCHEDULER_ENABLED: 'true',
});

function dependencies(fetchImpl) {
  return {
    createAppJwt: async () => 'opaque-app-assertion',
    fetch: fetchImpl,
    now: () => 1_800_000_000_000,
  };
}

function base64UrlToJson(segment) {
  const padded = segment.replaceAll('-', '+').replaceAll('_', '/') + '='.repeat((4 - segment.length % 4) % 4);
  return JSON.parse(Buffer.from(padded, 'base64').toString('utf8'));
}

test('Worker configuration uses the private, offset 15-minute Cron trigger', async () => {
  const configuration = JSON.parse(await readFile(new URL('../wrangler.jsonc', import.meta.url), 'utf8'));
  assert.equal(configuration.workers_dev, false);
  assert.deepEqual(configuration.triggers.crons, ['7,22,37,52 * * * *']);
});

test('App assertions use an in-memory PKCS#8 key and a bounded JWT lifetime', async () => {
  const keyPair = await crypto.subtle.generateKey(
    { hash: 'SHA-256', modulusLength: 2048, name: 'RSASSA-PKCS1-v1_5', publicExponent: new Uint8Array([1, 0, 1]) },
    true,
    ['sign', 'verify'],
  );
  const keyBytes = new Uint8Array(await crypto.subtle.exportKey('pkcs8', keyPair.privateKey));
  const privateKey = `-----BEGIN PRIVATE KEY-----\n${Buffer.from(keyBytes).toString('base64')}\n-----END PRIVATE KEY-----`;
  const assertion = await createGitHubAppJwt('123', privateKey, 1_800_000_000_000);
  const [header, payload, signature] = assertion.split('.');

  assert.deepEqual(base64UrlToJson(header), { alg: 'RS256', typ: 'JWT' });
  assert.deepEqual(base64UrlToJson(payload), { exp: 1_800_000_510, iat: 1_799_999_970, iss: '123' });
  assert.match(signature, /^[A-Za-z0-9_-]+$/);
});

test('scheduled dispatch exchanges an App assertion then dispatches only pages.yml on develop', async () => {
  const calls = [];
  const status = await dispatchPagesWorkflow(enabledEnvironment, dependencies(async (url, init) => {
    calls.push({ init, url: String(url) });
    if (String(url).endsWith('/access_tokens')) {
      return new Response(JSON.stringify({ token: 'opaque-installation-value' }), { status: 201 });
    }
    return new Response(null, { status: 204 });
  }));

  assert.equal(status, 204);
  assert.equal(calls.length, 2);
  assert.equal(calls[0].url, 'https://api.github.com/app/installations/456/access_tokens');
  assert.equal(calls[0].init.method, 'POST');
  assert.match(calls[0].init.headers.authorization, /^Bearer /);
  assert.equal(calls[1].url, 'https://api.github.com/repos/ThibaultPoujat/Tyrian_Ledger/actions/workflows/pages.yml/dispatches');
  assert.equal(calls[1].init.method, 'POST');
  assert.deepEqual(JSON.parse(calls[1].init.body), { ref: 'develop' });
  assert.match(calls[1].init.headers.authorization, /^Bearer /);
});

test('missing scheduler configuration fails before any GitHub request', async () => {
  let requested = false;
  await assert.rejects(
    dispatchPagesWorkflow({ SCHEDULER_ENABLED: 'true' }, dependencies(async () => {
      requested = true;
      return new Response(null, { status: 500 });
    })),
    (error) => error instanceof SchedulerFailure && error.code === 'configuration',
  );
  assert.equal(requested, false);
});

test('a disabled scheduler makes no network request', async () => {
  let requested = false;
  const messages = [];
  const result = await runScheduledDispatch({}, dependencies(async () => {
    requested = true;
    return new Response(null, { status: 500 });
  }), {
    error: (message, details) => messages.push({ details, message }),
    info: (message, details) => messages.push({ details, message }),
  });

  assert.equal(result, 'disabled');
  assert.equal(requested, false);
  assert.deepEqual(messages, [{ details: undefined, message: 'Pages scheduler is disabled.' }]);
});

test('failed responses report only operation and status, never response contents', async () => {
  const messages = [];
  await assert.rejects(
    runScheduledDispatch(enabledEnvironment, dependencies(async () => new Response('sensitive-response-content', { status: 401 })), {
      error: (message, details) => messages.push({ details, message }),
      info: (message, details) => messages.push({ details, message }),
    }),
    (error) => error instanceof SchedulerFailure && error.code === 'token-request' && error.status === 401,
  );

  assert.deepEqual(messages, [{
    details: { operation: 'token-request', status: 401 },
    message: 'Pages scheduler dispatch failed.',
  }]);
  assert.doesNotMatch(JSON.stringify(messages), /sensitive-response-content/);
});

test('a rejected workflow dispatch is reported without its response body', async () => {
  const messages = [];
  let callCount = 0;
  await assert.rejects(
    runScheduledDispatch(enabledEnvironment, dependencies(async () => {
      callCount += 1;
      return callCount === 1
        ? new Response(JSON.stringify({ token: 'opaque-installation-value' }), { status: 201 })
        : new Response('dispatch-response-content', { status: 422 });
    }), {
      error: (message, details) => messages.push({ details, message }),
      info: (message, details) => messages.push({ details, message }),
    }),
    (error) => error instanceof SchedulerFailure && error.code === 'dispatch-request' && error.status === 422,
  );

  assert.deepEqual(messages, [{
    details: { operation: 'dispatch-request', status: 422 },
    message: 'Pages scheduler dispatch failed.',
  }]);
  assert.doesNotMatch(JSON.stringify(messages), /dispatch-response-content/);
});

test('the Worker exposes no HTTP endpoint', async () => {
  const response = await worker.fetch(new Request('https://scheduler.invalid/'), enabledEnvironment, {
    passThroughOnException() {},
    waitUntil() {},
  });
  assert.equal(response.status, 404);
});
