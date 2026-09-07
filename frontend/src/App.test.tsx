import { cleanup, fireEvent, render, screen } from '@testing-library/react';
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

    if (input === '/api/local-data') {
      return Promise.resolve({
        ok: true,
        json: vi.fn().mockResolvedValue({
          databasePath: '/synthetic/Tyrian Ledger/tyrian-ledger.db',
          backupDirectoryPath: '/synthetic/Tyrian Ledger/backups',
        }),
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

describe('M14 local data controls', () => {
  it('shows the local foundation, safe no-key status, and guarded recovery controls', async () => {
    render(<App />);

    expect(screen.getByRole('heading', { name: 'The local application foundation is running.' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Local by default' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'A safe starting point' })).toBeVisible();
    expect(await screen.findByText('Local host connected')).toBeVisible();
    expect(await screen.findByText('No ArenaNet key configured')).toBeVisible();
    expect(screen.getByText(/never asks the browser to store or send it/i)).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Backup and recovery' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Create local backup' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Restore selected backup' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Clear personal account data' })).toBeDisabled();
    expect(screen.getByText('/synthetic/Tyrian Ledger/backups')).toBeVisible();
  });

  it('calls only same-origin safe contracts and does not use browser storage', async () => {
    render(<App />);

    expect(await screen.findByText('Local host connected')).toBeVisible();
    expect(fetch).toHaveBeenCalledWith('/api/health', expect.objectContaining({
      headers: { Accept: 'application/json' },
    }));
    expect(fetch).toHaveBeenCalledWith('/api/account-connection', expect.objectContaining({
      headers: {
        Accept: 'application/json',
        'X-Tyrian-Ledger-Request': '1',
      },
    }));
    expect(fetch).toHaveBeenCalledWith('/api/local-data', expect.objectContaining({
      headers: {
        Accept: 'application/json',
        'X-Tyrian-Ledger-Request': '1',
      },
    }));
    expect(Storage.prototype.getItem).not.toHaveBeenCalled();
    expect(Storage.prototype.setItem).not.toHaveBeenCalled();
    expect(Storage.prototype.removeItem).not.toHaveBeenCalled();
  });

  it('reports an unavailable host while leaving local recovery guidance available', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (input === '/api/health') {
        return Promise.reject(new TypeError('connection failed'));
      }

      if (input === '/api/local-data') {
        return Promise.resolve({
          ok: true,
          json: vi.fn().mockResolvedValue({
            databasePath: '/synthetic/Tyrian Ledger/tyrian-ledger.db',
            backupDirectoryPath: '/synthetic/Tyrian Ledger/backups',
          }),
        } as unknown as Response);
      }

      return Promise.resolve({
        ok: true,
        json: vi.fn().mockResolvedValue({
          state: 'not_configured',
          grantedPermissions: [],
          missingRequiredPermissions: ['account', 'tradingpost'],
        }),
      } as unknown as Response);
    });

    render(<App />);

    expect(await screen.findByText('Local host unavailable')).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Backup and recovery' })).toBeVisible();
  });

  it('keeps vault setup guidance available when native-store status is unavailable', async () => {
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
          state: 'unavailable',
          grantedPermissions: [],
          missingRequiredPermissions: ['account', 'tradingpost'],
        }),
      } as unknown as Response);
    });

    render(<App />);

    expect(await screen.findByText('ArenaNet key status unavailable')).toBeVisible();
    expect(screen.getByText(/store a dedicated read-only ArenaNet key/i)).toBeVisible();
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

  it('requires the exact clear confirmation before the browser can request deletion', async () => {
    render(<App />);

    const clearConfirmation = await screen.findByLabelText('Type CLEAR PERSONAL DATA to continue');
    fireEvent.change(clearConfirmation, { target: { value: 'CLEAR PERSONAL DATA' } });
    expect(screen.getByRole('button', { name: 'Clear personal account data' })).toBeEnabled();
  });

  it('requires both a selected backup and the exact restore confirmation', async () => {
    render(<App />);

    const restoreFile = await screen.findByLabelText('Backup file');
    fireEvent.change(restoreFile, { target: { files: [new File(['synthetic'], 'backup.db', { type: 'application/x-sqlite3' })] } });
    fireEvent.change(screen.getByLabelText('Type RESTORE LOCAL DATA to continue'), { target: { value: 'RESTORE LOCAL DATA' } });

    expect(screen.getByRole('button', { name: 'Restore selected backup' })).toBeEnabled();
  });

  it('makes recovery actions mutually exclusive while one is running', async () => {
    let completeBackup: (value: Response) => void;
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (input === '/api/health') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'healthy' }) } as unknown as Response);
      }
      if (input === '/api/local-data') {
        return Promise.resolve({
          ok: true,
          json: vi.fn().mockResolvedValue({ databasePath: '/synthetic/tyrian-ledger.db', backupDirectoryPath: '/synthetic/backups' }),
        } as unknown as Response);
      }
      if (input === '/api/local-data/backup') {
        return new Promise((resolve) => {
          completeBackup = resolve;
        });
      }
      return Promise.resolve({
        ok: true,
        json: vi.fn().mockResolvedValue({ state: 'not_configured', grantedPermissions: [], missingRequiredPermissions: [] }),
      } as unknown as Response);
    });
    render(<App />);

    const restoreFile = await screen.findByLabelText('Backup file');
    fireEvent.change(restoreFile, { target: { files: [new File(['synthetic'], 'backup.db', { type: 'application/x-sqlite3' })] } });
    fireEvent.change(screen.getByLabelText('Type RESTORE LOCAL DATA to continue'), { target: { value: 'RESTORE LOCAL DATA' } });
    fireEvent.change(screen.getByLabelText('Type CLEAR PERSONAL DATA to continue'), { target: { value: 'CLEAR PERSONAL DATA' } });
    fireEvent.click(screen.getByRole('button', { name: 'Create local backup' }));

    expect(await screen.findByRole('button', { name: 'Creating backup…' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Restore selected backup' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Clear personal account data' })).toBeDisabled();

    completeBackup!({ ok: true, json: vi.fn().mockResolvedValue({ fileName: 'synthetic.db' }) } as unknown as Response);
    expect(await screen.findByText('Backup created: synthetic.db')).toBeVisible();
  });

  it('resets the restore selection after a confirmed restore', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (input === '/api/health') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'healthy' }) } as unknown as Response);
      }
      if (input === '/api/local-data') {
        return Promise.resolve({
          ok: true,
          json: vi.fn().mockResolvedValue({ databasePath: '/synthetic/tyrian-ledger.db', backupDirectoryPath: '/synthetic/backups' }),
        } as unknown as Response);
      }
      if (input === '/api/local-data/restore') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'restored' }) } as unknown as Response);
      }
      return Promise.resolve({
        ok: true,
        json: vi.fn().mockResolvedValue({ state: 'not_configured', grantedPermissions: [], missingRequiredPermissions: [] }),
      } as unknown as Response);
    });
    render(<App />);

    const restoreFile = await screen.findByLabelText('Backup file');
    fireEvent.change(restoreFile, { target: { files: [new File(['synthetic'], 'backup.db', { type: 'application/x-sqlite3' })] } });
    fireEvent.change(screen.getByLabelText('Type RESTORE LOCAL DATA to continue'), { target: { value: 'RESTORE LOCAL DATA' } });
    fireEvent.click(screen.getByRole('button', { name: 'Restore selected backup' }));

    expect(await screen.findByText('Backup restored.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Restore selected backup' })).toBeDisabled();
  });

  it('reports an unknown outcome when restore or clear loses its response', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (input === '/api/health') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'healthy' }) } as unknown as Response);
      }
      if (input === '/api/local-data') {
        return Promise.resolve({
          ok: true,
          json: vi.fn().mockResolvedValue({ databasePath: '/synthetic/tyrian-ledger.db', backupDirectoryPath: '/synthetic/backups' }),
        } as unknown as Response);
      }
      if (input === '/api/local-data/restore' || input === '/api/local-data/clear-personal') {
        return Promise.reject(new TypeError('connection lost'));
      }
      return Promise.resolve({
        ok: true,
        json: vi.fn().mockResolvedValue({ state: 'not_configured', grantedPermissions: [], missingRequiredPermissions: [] }),
      } as unknown as Response);
    });
    render(<App />);

    const restoreFile = await screen.findByLabelText('Backup file');
    fireEvent.change(restoreFile, { target: { files: [new File(['synthetic'], 'backup.db', { type: 'application/x-sqlite3' })] } });
    fireEvent.change(screen.getByLabelText('Type RESTORE LOCAL DATA to continue'), { target: { value: 'RESTORE LOCAL DATA' } });
    fireEvent.click(screen.getByRole('button', { name: 'Restore selected backup' }));
    expect(await screen.findByText('Restore outcome could not be confirmed. Check local data before retrying.')).toBeVisible();

    fireEvent.change(screen.getByLabelText('Type CLEAR PERSONAL DATA to continue'), { target: { value: 'CLEAR PERSONAL DATA' } });
    fireEvent.click(screen.getByRole('button', { name: 'Clear personal account data' }));
    expect(await screen.findByText('Clear outcome could not be confirmed. Check local data before retrying.')).toBeVisible();
  });
});
