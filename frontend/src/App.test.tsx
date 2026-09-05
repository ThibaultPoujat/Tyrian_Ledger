import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
    if (input === '/api/health') {
      return Promise.resolve({
        ok: true,
        json: vi.fn().mockResolvedValue({ status: 'healthy' }),
      });
    }

    return Promise.resolve({
      ok: true,
      json: vi.fn().mockResolvedValue({
        state: 'not_configured',
        grantedPermissions: [],
        missingRequiredPermissions: ['account', 'tradingpost'],
      }),
    });
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
  it('shows the local foundation and safe no-key status without offering account or trading actions', async () => {
    render(<App />);

    expect(screen.getByRole('heading', { name: 'The local application foundation is running.' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Local by default' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'A safe starting point' })).toBeVisible();
    expect(await screen.findByText('Local host connected')).toBeVisible();
    expect(await screen.findByText('No ArenaNet key configured')).toBeVisible();
    expect(screen.getByText(/never asks the browser to store or send it/i)).toBeVisible();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('calls only same-origin safe contracts and does not use browser storage', async () => {
    render(<App />);

    expect(await screen.findByText('Local host connected')).toBeVisible();
    expect(fetch).toHaveBeenCalledWith('/api/health', expect.objectContaining({
      headers: { Accept: 'application/json' },
    }));
    expect(fetch).toHaveBeenCalledWith('/api/account-connection', expect.objectContaining({
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

  it('explains missing permissions without treating untrusted data as markup', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (input === '/api/health') {
        return Promise.resolve({
          ok: true,
          json: vi.fn().mockResolvedValue({ status: 'healthy' }),
        } as unknown as Response);
      }

      return Promise.resolve({
        ok: true,
        json: vi.fn().mockResolvedValue({
          state: 'insufficient_permissions',
          grantedPermissions: ['account'],
          missingRequiredPermissions: ['tradingpost'],
          name: '<img src=x onerror=alert(1)>',
        }),
      } as unknown as Response);
    });

    render(<App />);

    expect(await screen.findByText('ArenaNet key needs permission: tradingpost')).toBeVisible();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(document.body.innerHTML).not.toContain('<img src=x');
  });
});
