import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';

const notSynchronizedDashboard = {
  state: 'notSynchronized', accountError: null, lastSuccessfulSyncAtUtc: null, currentOrdersObservedAtUtc: null,
  historyCoverage: null, marketState: 'unavailable', isFeeRoundingExternallyVerified: false,
  realizedWindows: [], openAcquisitionBasis: null, netLiquidationValue: null, unrealizedProfit: null,
  isOpenInventoryFullyValued: null, openInventory: [], currentBuyCapital: { copper: '0' },
  currentSellGrossValue: { copper: '0' }, currentSellNetValue: { copper: '0' }, currentOrders: [],
  recentTrades: [], bestRealizedItems: [], worstRealizedItems: [],
};

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

    if (input === '/api/personal-dashboard') {
      return Promise.resolve({
        ok: true,
        json: vi.fn().mockResolvedValue(notSynchronizedDashboard),
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
  it('renders backend-authoritative scanner evidence and toggles a durable watchlist entry', async () => {
    let watched = false;
    const scanner = {
      state: 'ready', error: null, observedAtUtc: '2026-09-08T12:00:00Z', isFeeRoundingExternallyVerified: false, isTruncated: false,
      exclusions: [{ reason: 'feeLosing', count: 2 }],
      candidates: [{ itemId: 42, itemName: 'Scanner item', bestBuy: { quantity: 5, unitPrice: { copper: '100' } }, lowestSell: { quantity: 8, unitPrice: { copper: '200' } }, plannedBid: { copper: '100' }, plannedListPrice: { copper: '200' }, netProfit: { copper: '70' }, totalCost: { copper: '105' }, maximumBid: { copper: '120' }, modeledRoi: { profit: { copper: '70' }, totalCost: { copper: '105' }, displayPercent: '66.67%' }, liquidity: { participationCapQuantity: 3, reasons: ['buyPriceCliff'], acquisition: { requestedQuantity: 1, filledQuantity: 1, isFullyFilled: true, totalValue: { copper: '110' } }, liquidation: { requestedQuantity: 1, filledQuantity: 1, isFullyFilled: true, totalValue: { copper: '200' } }, topBuyLevels: [{ listings: 2, quantity: 5, unitPrice: { copper: '100' } }], topSellLevels: [{ listings: 3, quantity: 8, unitPrice: { copper: '200' } }] } }],
    };
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      if (typeof input === 'string' && input.startsWith('/api/live-market-scanner')) return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(scanner) } as unknown as Response);
      if (input === '/api/watchlist') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ itemIds: watched ? [42] : [] }) } as unknown as Response);
      if (input === '/api/watchlist/42' && init?.method === 'PUT') { watched = true; return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'watching' }) } as unknown as Response); }
      if (input === '/api/watchlist/42' && init?.method === 'DELETE') { watched = false; return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'removed' }) } as unknown as Response); }
      if (input === '/api/health') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'healthy' }) } as unknown as Response);
      if (input === '/api/local-data') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ databasePath: '/synthetic/db', backupDirectoryPath: '/synthetic/backups' }) } as unknown as Response);
      if (input === '/api/personal-dashboard') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(notSynchronizedDashboard) } as unknown as Response);
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'not_configured', grantedPermissions: [], missingRequiredPermissions: [] }) } as unknown as Response);
    });

    render(<App />);
    expect(await screen.findByRole('heading', { name: 'Current scanner and watchlist' })).toBeVisible();
    expect(await screen.findByText('Scanner item')).toBeVisible();
    expect(screen.getByText('66.67%')).toBeVisible();
    expect(screen.getByText(/Risk: Buy Price Cliff/)).toBeVisible();
    fireEvent.click(screen.getByText('Order-book detail'));
    expect(screen.getByRole('heading', { name: 'Buy orders' })).toBeVisible();
    fireEvent.change(screen.getByLabelText('Max planned bid (copper)'), { target: { value: '99' } });
    expect(screen.getByText('No current candidates match these presentation filters.')).toBeVisible();
    fireEvent.change(screen.getByLabelText('Max planned bid (copper)'), { target: { value: '100' } });
    expect(screen.getByText('Scanner item')).toBeVisible();
    fireEvent.change(screen.getByLabelText('Max per-unit capital (copper)'), { target: { value: '104' } });
    expect(screen.getByText('No current candidates match these presentation filters.')).toBeVisible();
    fireEvent.change(screen.getByLabelText('Max per-unit capital (copper)'), { target: { value: '105' } });
    fireEvent.click(screen.getByRole('checkbox', { name: 'No liquidity flags only' }));
    expect(screen.getByText('No current candidates match these presentation filters.')).toBeVisible();
    fireEvent.click(screen.getByRole('checkbox', { name: 'No liquidity flags only' }));
    fireEvent.change(screen.getByLabelText('Minimum ROI (basis points)'), { target: { value: '250' } });
    fireEvent.change(screen.getByLabelText('Minimum modeled profit (copper)'), { target: { value: '50' } });
    fireEvent.change(screen.getByLabelText('Intended quantity'), { target: { value: '2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Refresh scanner' }));
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/live-market-scanner?minimumRoiBasisPoints=250&minimumNetProfitCopper=50&intendedQuantity=2', expect.anything()));
    fireEvent.click(screen.getByRole('button', { name: 'Add to watchlist' }));
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/watchlist/42', expect.objectContaining({ method: 'PUT' })));
    expect(await screen.findByRole('button', { name: 'Remove from watchlist' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Remove from watchlist' }));
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/watchlist/42', expect.objectContaining({ method: 'DELETE' })));
    expect(Storage.prototype.setItem).not.toHaveBeenCalled();
  });

  it('distinguishes an empty scanner from an unavailable durable watchlist', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (typeof input === 'string' && input.startsWith('/api/live-market-scanner')) return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'ready', error: null, observedAtUtc: null, isFeeRoundingExternallyVerified: true, isTruncated: false, candidates: [], exclusions: [] }) } as unknown as Response);
      if (input === '/api/watchlist') return Promise.reject(new TypeError('unavailable'));
      if (input === '/api/health') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'healthy' }) } as unknown as Response);
      if (input === '/api/local-data') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ databasePath: '/synthetic/db', backupDirectoryPath: '/synthetic/backups' }) } as unknown as Response);
      if (input === '/api/personal-dashboard') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(notSynchronizedDashboard) } as unknown as Response);
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'not_configured', grantedPermissions: [], missingRequiredPermissions: [] }) } as unknown as Response);
    });

    render(<App />);

    expect(await screen.findByText('No current candidates match these presentation filters.')).toBeVisible();
    expect(await screen.findByText('Watchlist unavailable.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Retry' })).toBeVisible();
  });

  it('shows the local foundation, safe no-key status, and guarded recovery controls', async () => {
    render(<App />);

    expect(screen.getByRole('heading', { name: 'Understand your trading position.' })).toBeVisible();
    expect(await screen.findByText('Local host connected')).toBeVisible();
    expect(await screen.findByText('No ArenaNet key configured')).toBeVisible();
    expect(await screen.findByRole('heading', { name: 'No personal data yet' })).toBeVisible();
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
    expect(fetch).toHaveBeenCalledWith('/api/personal-dashboard', expect.objectContaining({
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

  it('renders backend-authoritative performance, unknown basis, and current-order states', async () => {
    const dashboard = {
      state: 'ready', accountError: null, lastSuccessfulSyncAtUtc: '2026-09-08T12:00:00Z', currentOrdersObservedAtUtc: '2026-09-08T12:00:00Z',
      historyCoverage: { startUtc: '2026-06-10T12:00:00Z', endUtc: '2026-09-08T12:00:00Z' }, marketState: 'available', isFeeRoundingExternallyVerified: false,
      realizedWindows: [{ days: 7, status: 'supported', netProfit: { copper: '70' }, unknownBasisQuantity: 2 }, { days: 30, status: 'insufficientCoverage', netProfit: null, unknownBasisQuantity: 0 }],
      openAcquisitionBasis: { copper: '100' }, netLiquidationValue: { copper: '170' }, unrealizedProfit: { copper: '70' }, isOpenInventoryFullyValued: true,
      openInventory: [{ itemId: 42, itemName: 'Test item', quantity: 1, acquisitionBasis: { copper: '100' }, liquidationStatus: 'fullyValued', unliquidatedQuantity: 0, netLiquidationValue: { copper: '170' }, unrealizedProfit: { copper: '70' } }],
      currentBuyCapital: { copper: '9007199254740993' }, currentSellGrossValue: { copper: '600' }, currentSellNetValue: { copper: '510' },
      currentOrders: [{ orderId: '1', side: 'buy', itemId: 42, itemName: 'Test item', quantity: 2, unitPrice: { copper: '50' }, marketComparisonStatus: 'available', currentMarketUnitPrice: { copper: '60' } }],
      recentTrades: [{ transactionId: '2', side: 'sell', itemName: 'Test item', quantity: 1, unitPrice: { copper: '200' }, completedAtUtc: '2026-09-08T12:00:00Z' }],
      bestRealizedItems: [{ itemId: 42, itemName: 'Test item', quantity: 1, netProfit: { copper: '70' } }], worstRealizedItems: [],
    };
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (input === '/api/health') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'healthy' }) } as unknown as Response);
      if (input === '/api/account-connection') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'valid', grantedPermissions: ['account', 'tradingpost'], missingRequiredPermissions: [] }) } as unknown as Response);
      if (input === '/api/local-data') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ databasePath: '/synthetic/tyrian-ledger.db', backupDirectoryPath: '/synthetic/backups' }) } as unknown as Response);
      if (input === '/api/personal-dashboard') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(dashboard) } as unknown as Response);
      if (input === '/api/personal-trading-post/sync') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'succeeded' }) } as unknown as Response);
      return Promise.reject(new TypeError('unexpected request'));
    });

    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Performance and current orders' })).toBeVisible();
    expect(screen.getByText('900719925474g 9s 93c')).toBeVisible();
    expect(screen.getAllByText('70c', { exact: false })).not.toHaveLength(0);
    expect(screen.getByText('2 sold without known basis, excluded.')).toBeVisible();
    expect(screen.getByText('Insufficient coverage')).toBeVisible();
    expect(screen.getByRole('columnheader', { name: 'Current market' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Synchronize Trading Post data' }));
    expect(await screen.findByRole('heading', { name: 'Performance and current orders' })).toBeVisible();
    expect(fetch).toHaveBeenCalledWith('/api/personal-trading-post/sync', expect.objectContaining({ method: 'POST' }));
  });

  it('shows loading, malformed data, and account-unavailable dashboard states safely', async () => {
    let resolveDashboard: (response: Response) => void;
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (input === '/api/personal-dashboard') return new Promise((resolve) => { resolveDashboard = resolve; });
      if (input === '/api/health') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'healthy' }) } as unknown as Response);
      if (input === '/api/local-data') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ databasePath: '/synthetic/tyrian-ledger.db', backupDirectoryPath: '/synthetic/backups' }) } as unknown as Response);
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'valid', grantedPermissions: [], missingRequiredPermissions: [] }) } as unknown as Response);
    });
    render(<App />);
    expect(screen.getByRole('heading', { name: 'Loading personal dashboard…' })).toBeVisible();
    resolveDashboard!({ ok: true, json: vi.fn().mockResolvedValue({ state: 'ready' }) } as unknown as Response);
    expect(await screen.findByRole('heading', { name: 'Dashboard unavailable' })).toBeVisible();

    cleanup();
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (input === '/api/personal-dashboard') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ ...notSynchronizedDashboard, state: 'accountUnavailable', accountError: 'upstreamUnavailable' }) } as unknown as Response);
      if (input === '/api/health') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'healthy' }) } as unknown as Response);
      if (input === '/api/local-data') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ databasePath: '/synthetic/tyrian-ledger.db', backupDirectoryPath: '/synthetic/backups' }) } as unknown as Response);
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'valid', grantedPermissions: [], missingRequiredPermissions: [] }) } as unknown as Response);
    });
    render(<App />);
    expect(await screen.findByRole('heading', { name: 'Connect an account to view your dashboard' })).toBeVisible();
  });

  it('renders incomplete coverage and unavailable liquidation evidence explicitly', async () => {
    const incompleteDashboard = {
      ...notSynchronizedDashboard,
      state: 'ready', historyCoverage: { startUtc: null, endUtc: null }, isOpenInventoryFullyValued: false,
      openInventory: [
        { itemId: 42, itemName: 'Shallow item', quantity: 2, acquisitionBasis: { copper: '100' }, liquidationStatus: 'insufficientBuyDepth', unliquidatedQuantity: 1, netLiquidationValue: null, unrealizedProfit: null },
        { itemId: 84, itemName: 'Unpriced item', quantity: 1, acquisitionBasis: { copper: '100' }, liquidationStatus: 'evidenceMissing', unliquidatedQuantity: 1, netLiquidationValue: null, unrealizedProfit: null },
      ],
    };
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (input === '/api/personal-dashboard') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(incompleteDashboard) } as unknown as Response);
      if (input === '/api/health') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'healthy' }) } as unknown as Response);
      if (input === '/api/local-data') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ databasePath: '/synthetic/tyrian-ledger.db', backupDirectoryPath: '/synthetic/backups' }) } as unknown as Response);
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'valid', grantedPermissions: [], missingRequiredPermissions: [] }) } as unknown as Response);
    });
    render(<App />);
    expect(await screen.findByText(/Continuous history coverage is not available/)).toBeVisible();
    expect(screen.getByText('Not fully valued')).toBeVisible();
    expect(screen.getByText('Insufficient buy depth (1 remaining)')).toBeVisible();
    expect(screen.getByText('Market evidence missing')).toBeVisible();
  });

  it('does not let an older dashboard response replace a newer refresh', async () => {
    const dashboardResponses: Array<(response: Response) => void> = [];
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (input === '/api/personal-dashboard') return new Promise((resolve) => { dashboardResponses.push(resolve); });
      if (input === '/api/personal-trading-post/sync') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'succeeded' }) } as unknown as Response);
      if (input === '/api/health') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'healthy' }) } as unknown as Response);
      if (input === '/api/local-data') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ databasePath: '/synthetic/tyrian-ledger.db', backupDirectoryPath: '/synthetic/backups' }) } as unknown as Response);
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'valid', grantedPermissions: [], missingRequiredPermissions: [] }) } as unknown as Response);
    });
    render(<App />);
    await waitFor(() => expect(dashboardResponses).toHaveLength(1));
    fireEvent.click(await screen.findByRole('button', { name: 'Synchronize Trading Post data' }));
    await waitFor(() => expect(dashboardResponses).toHaveLength(2));

    dashboardResponses[1]({ ok: true, json: vi.fn().mockResolvedValue(notSynchronizedDashboard) } as unknown as Response);
    expect(await screen.findByRole('heading', { name: 'No personal data yet' })).toBeVisible();
    dashboardResponses[0]({ ok: true, json: vi.fn().mockResolvedValue({ state: 'ready' }) } as unknown as Response);
    await Promise.resolve();
    expect(screen.getByRole('heading', { name: 'No personal data yet' })).toBeVisible();
  });

  it('refreshes the dashboard after confirmed restore and clear outcomes', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      if (input === '/api/personal-dashboard') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(notSynchronizedDashboard) } as unknown as Response);
      if (input === '/api/health') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'healthy' }) } as unknown as Response);
      if (input === '/api/local-data') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ databasePath: '/synthetic/tyrian-ledger.db', backupDirectoryPath: '/synthetic/backups' }) } as unknown as Response);
      if (input === '/api/local-data/restore') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'restored' }) } as unknown as Response);
      if (input === '/api/local-data/clear-personal') return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ outcome: 'personal_data_cleared' }) } as unknown as Response);
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ state: 'valid', grantedPermissions: [], missingRequiredPermissions: [] }) } as unknown as Response);
    });
    render(<App />);
    await screen.findByRole('heading', { name: 'No personal data yet' });
    fireEvent.change(screen.getByLabelText('Backup file'), { target: { files: [new File(['synthetic'], 'backup.db', { type: 'application/x-sqlite3' })] } });
    fireEvent.change(screen.getByLabelText('Type RESTORE LOCAL DATA to continue'), { target: { value: 'RESTORE LOCAL DATA' } });
    fireEvent.click(screen.getByRole('button', { name: 'Restore selected backup' }));
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.filter(([path]) => path === '/api/personal-dashboard')).toHaveLength(2));
    fireEvent.change(screen.getByLabelText('Type CLEAR PERSONAL DATA to continue'), { target: { value: 'CLEAR PERSONAL DATA' } });
    fireEvent.click(screen.getByRole('button', { name: 'Clear personal account data' }));
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.filter(([path]) => path === '/api/personal-dashboard')).toHaveLength(3));
  });
});
