import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { invalidateViewCache, resetViewCacheForTests, useViewQuery } from './viewQueryCache';

type Payload = { value: string; scope?: string };
const validate = (value: unknown): value is Payload => typeof value === 'object' && value !== null && typeof (value as Payload).value === 'string';

function View({ name = 'plans' }: { name?: string }) {
  const query = useViewQuery({ key: name, url: `/api/${name}`, validate, scopeFrom: payload => payload.scope });
  if (query.phase === 'loading') return <p>Chargement initial</p>;
  return <div><span>{query.data?.value}</span>{query.phase === 'refreshing' && <small>Actualisation discrète</small>}<button disabled={query.isStale || query.phase === 'refreshing'}>Action</button></div>;
}

afterEach(() => { resetViewCacheForTests(); vi.unstubAllGlobals(); });

it('deduplicates concurrent view reads and keeps cached data visible during a slow revisit', async () => {
  let resolve!: (value: unknown) => void;
  const pending = new Promise<unknown>(done => { resolve = done; });
  const fetchMock = vi.fn().mockImplementation(() => pending.then(payload => ({ ok: true, json: async () => payload })));
  vi.stubGlobal('fetch', fetchMock);

  const first = render(<><View /><View /></>);
  expect(screen.getAllByText('Chargement initial')).toHaveLength(2);
  expect(fetchMock).toHaveBeenCalledTimes(1);
  resolve({ value: 'Plan chargé', scope: 'opaque-a' });
  expect(await screen.findAllByText('Plan chargé')).toHaveLength(2);

  first.unmount();
  render(<View />);
  expect(screen.getByText('Plan chargé')).toBeInTheDocument();
  expect(screen.getByText('Actualisation discrète')).toBeInTheDocument();
  expect(fetchMock).toHaveBeenCalledTimes(2);
});

it('marks cached mutations non-executable until the invalidated view has refreshed', async () => {
  let resolve!: (value: unknown) => void;
  const delayed = new Promise<unknown>(done => { resolve = done; });
  let calls = 0;
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => {
    calls++;
    const payload = calls === 1 ? Promise.resolve({ value: 'Plan initial', scope: 'opaque-a' }) : delayed;
    return payload.then(value => ({ ok: true, json: async () => value }));
  }));
  render(<View />);
  expect(await screen.findByText('Plan initial')).toBeInTheDocument();
  invalidateViewCache(['plans']);
  await waitFor(() => expect(screen.getByRole('button', { name: 'Action' })).toBeDisabled());
  resolve({ value: 'Plan confirmé', scope: 'opaque-a' });
  await waitFor(() => expect(screen.getByText('Plan confirmé')).toBeInTheDocument());
  expect(screen.getByRole('button', { name: 'Action' })).not.toBeDisabled();
});

it('does not reuse cached content after an opaque account scope changes', async () => {
  const responses = [{ value: 'Compte A', scope: 'opaque-a' }, { value: 'Compte B', scope: 'opaque-b' }];
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve({ ok: true, json: async () => responses.shift() })));
  const first = render(<View name="recommendations" />);
  expect(await screen.findByText('Compte A')).toBeInTheDocument();
  first.unmount();
  render(<View name="recommendations" />);
  expect(screen.getByText('Compte A')).toBeInTheDocument();
  expect(await screen.findByText('Compte B')).toBeInTheDocument();
});
