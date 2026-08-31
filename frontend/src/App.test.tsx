import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import App from './App';

afterEach(() => {
  cleanup();
  window.history.replaceState({}, '', '/');
});

describe('M9 application shell', () => {
  it('exposes Recommendations and Settings, with Recommendations as the default route', () => {
    render(<App />);

    expect(screen.getByRole('heading', { name: 'Recommendations' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'Recommendations' })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: 'Settings' })).toHaveAttribute('href', '/settings');
    expect(screen.queryByText(/market opportunities|crafting analysis|personal history/i)).not.toBeInTheDocument();
  });

  it('renders the Settings placeholder without exposing legacy controls', () => {
    window.history.replaceState({}, '', '/settings');
    render(<App />);

    expect(screen.getByRole('heading', { name: 'Settings' })).toBeVisible();
    expect(screen.queryByRole('button', { name: /save and apply preferences|clear account/i })).not.toBeInTheDocument();
  });

  it('makes retired browser paths unavailable', () => {
    window.history.replaceState({}, '', '/history');
    render(<App />);

    expect(screen.getByTestId('unavailable-route')).toHaveTextContent('Route unavailable');
    expect(screen.getByRole('link', { name: 'Go to Recommendations' })).toHaveAttribute('href', '/recommendations');
  });
});
