import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
    ok: true,
    json: vi.fn().mockResolvedValue({ status: 'healthy' }),
  }));
  vi.spyOn(Storage.prototype, 'getItem');
  vi.spyOn(Storage.prototype, 'setItem');
  vi.spyOn(Storage.prototype, 'removeItem');
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('M13 local host shell', () => {
  it('shows the local foundation without offering account or trading actions', async () => {
    render(<App />);

    expect(screen.getByRole('heading', { name: 'The local application foundation is running.' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Local by default' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'A safe starting point' })).toBeVisible();
    expect(await screen.findByRole('status')).toHaveTextContent('Local host connected');
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('calls only the same-origin health contract and does not use browser storage', async () => {
    render(<App />);

    expect(await screen.findByText('Local host connected')).toBeVisible();
    expect(fetch).toHaveBeenCalledWith('/api/health', expect.objectContaining({
      headers: { Accept: 'application/json' },
    }));
    expect(Storage.prototype.getItem).not.toHaveBeenCalled();
    expect(Storage.prototype.setItem).not.toHaveBeenCalled();
    expect(Storage.prototype.removeItem).not.toHaveBeenCalled();
  });

  it('reports an unavailable host without exposing another feature path', async () => {
    vi.mocked(fetch).mockRejectedValueOnce(new TypeError('connection failed'));

    render(<App />);

    expect(await screen.findByText('Local host unavailable')).toBeVisible();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
