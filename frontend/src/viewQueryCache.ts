import { useCallback, useEffect, useState } from 'react';

export type QueryPhase = 'loading' | 'refreshing' | 'ready' | 'error';

type Entry<T> = {
  data: T | null;
  error: boolean;
  stale: boolean;
  promise: Promise<boolean> | null;
  updatedAt: number | null;
};

type QueryOptions<T> = {
  key: string;
  url: string;
  init?: RequestInit;
  validate: (value: unknown) => value is T;
  acceptResponse?: (response: Response, value: T) => boolean;
  discardOnError?: boolean;
  /** An opaque server-issued token. It never contains account identity or credentials. */
  scopeFrom?: (value: T) => string | null | undefined;
};

const entries = new Map<string, Entry<unknown>>();
const listeners = new Set<() => void>();
let scope = 'initial';

function scopedKey(key: string) { return `${scope}:${key}`; }
function notify() { listeners.forEach(listener => listener()); }
function entryFor<T>(key: string): Entry<T> {
  const existing = entries.get(scopedKey(key));
  if (existing) return existing as Entry<T>;
  const entry: Entry<T> = { data: null, error: false, stale: false, promise: null, updatedAt: null };
  entries.set(scopedKey(key), entry as Entry<unknown>);
  return entry;
}

export function setViewCacheScope(nextScope: string | null | undefined) {
  if (!nextScope || nextScope === scope) return;
  scope = nextScope;
  notify();
}

export function invalidateViewCache(keys?: readonly string[]) {
  const selected = keys === undefined ? null : new Set(keys);
  for (const [key, entry] of entries) {
    if (selected === null || selected.has(key.slice(key.indexOf(':') + 1))) entry.stale = true;
  }
  notify();
}

export function putViewCacheData<T>(key: string, data: T) {
  const entry = entryFor<T>(key);
  entry.data = data;
  entry.error = false;
  entry.stale = false;
  entry.updatedAt = Date.now();
  notify();
}

export function clearViewCacheForAccountChange() {
  entries.clear();
  scope = 'initial';
  notify();
}

export function resetViewCacheForTests() {
  entries.clear();
  scope = 'initial';
  listeners.clear();
}

async function fetchEntry<T>(options: QueryOptions<T>, override?: RequestInit): Promise<boolean> {
  const requestScope = scope;
  const entry = entryFor<T>(options.key);
  if (entry.promise) return entry.promise;
  entry.error = false;
  const startedAt = performance.now();
  entry.promise = (async () => {
    try {
      const response = await fetch(options.url, { ...options.init, ...override });
      const payload: unknown = await response.json();
      if (!options.validate(payload) || !(options.acceptResponse?.(response, payload) ?? response.ok)) throw new Error('invalid_query_response');
      const data = payload;
      const responseScope = options.scopeFrom?.(data);
      if (responseScope) setViewCacheScope(responseScope);
      const target = responseScope && responseScope !== requestScope
        ? entryFor<T>(options.key)
        : entry;
      target.data = data;
      target.error = false;
      target.stale = false;
      target.updatedAt = Date.now();
      // Timings stay in memory/devtools only; no account data is persisted by the browser.
      try { performance.measure(`tyrian-ledger:${options.key}`, { start: startedAt, end: performance.now() }); } catch { /* optional instrumentation */ }
      return true;
    } catch {
      entry.error = true;
      if (entry.data !== null) entry.stale = true;
      if (options.discardOnError) entry.data = null;
      return false;
    } finally {
      entry.promise = null;
      notify();
    }
  })();
  notify();
  return entry.promise;
}

export function useViewQuery<T>(options: QueryOptions<T>) {
  const [version, render] = useState(0);
  const [activated, setActivated] = useState(false);
  const read = useCallback(() => entryFor<T>(options.key), [options.key]);
  useEffect(() => {
    const listener = () => render(value => value + 1);
    listeners.add(listener);
    return () => { listeners.delete(listener); };
  }, []);
  useEffect(() => {
    setActivated(true);
    void fetchEntry(options);
  }, [options.key, options.url]);
  useEffect(() => {
    const entry = entryFor<T>(options.key);
    if (!entry.error && entry.stale && entry.promise === null) void fetchEntry(options);
  }, [version]);
  const refresh = useCallback((override?: RequestInit) => fetchEntry(options, override), [options]);
  const entry = read();
  const phase: QueryPhase = entry.data === null
    ? entry.error ? 'error' : 'loading'
    : entry.promise !== null ? 'refreshing' : 'ready';
  return { data: entry.data, phase, isStale: entry.stale || (!activated && entry.data !== null), updatedAt: entry.updatedAt, refresh };
}
