import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import App from './App';

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn());
  vi.spyOn(Storage.prototype, 'getItem');
  vi.spyOn(Storage.prototype, 'setItem');
  vi.spyOn(Storage.prototype, 'removeItem');
});

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('M12 transition shell', () => {
  it('explains that the public runtime was retired without offering recommendations', () => {
    render(<App />);

    expect(screen.getByRole('heading', { name: 'The public trading assistant has been retired.' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'What remains' })).toBeVisible();
    expect(screen.getByRole('heading', { name: 'What comes next' })).toBeVisible();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.queryByText(/market snapshot/i, { selector: 'button, a' })).not.toBeInTheDocument();
  });

  it('does not fetch, read, or write browser state', () => {
    render(<App />);

    expect(fetch).not.toHaveBeenCalled();
    expect(Storage.prototype.getItem).not.toHaveBeenCalled();
    expect(Storage.prototype.setItem).not.toHaveBeenCalled();
    expect(Storage.prototype.removeItem).not.toHaveBeenCalled();
  });
});
