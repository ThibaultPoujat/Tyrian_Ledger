import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { copperFromMoneyInput, createFixtureProvider } from './fixtureProvider';
import { SignalsExperience } from './SignalsExperience';

afterEach(cleanup);

describe('SignalsExperience fixture preview', () => {
  it('keeps draft edits out of the session until Apply and discards them on Cancel', () => {
    render(<SignalsExperience provider={createFixtureProvider('normal')} />);
    fireEvent.click(screen.getByRole('button', { name: 'Adapter ma session' }));
    fireEvent.change(screen.getByLabelText('Durée personnalisée en minutes'), { target: { value: '5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Annuler' }));

    expect(screen.getByRole('button', { name: '15 min' })).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(screen.getByRole('button', { name: 'Adapter ma session' }));
    expect(screen.getByLabelText('Durée personnalisée en minutes')).toHaveValue(15);
  });

  it('applies a deterministic empty fixture when a short session cannot fit the prepared examples', () => {
    render(<SignalsExperience provider={createFixtureProvider('normal')} />);
    fireEvent.click(screen.getByRole('button', { name: 'Adapter ma session' }));
    fireEvent.change(screen.getByLabelText('Durée personnalisée en minutes'), { target: { value: '5' } });
    fireEvent.click(screen.getByRole('button', { name: 'Appliquer à cette session' }));

    expect(screen.getByRole('heading', { name: 'Aucun signal ne mérite votre attention pour le moment.' })).toBeVisible();
    expect(screen.getByText('Aucun signal fictif ne répond à ces seuils de session.')).toBeVisible();
  });

  it('uses the fixture provider for deadline results instead of treating future sales as cash', () => {
    render(<SignalsExperience provider={createFixtureProvider('normal')} />);
    fireEvent.click(screen.getByRole('button', { name: 'Adapter ma session' }));
    fireEvent.click(screen.getByRole('button', { name: /Or disponible avant/ }));
    fireEvent.click(screen.getByRole('button', { name: 'Appliquer à cette session' }));

    expect(screen.getByRole('heading', { name: 'Vendre votre surplus' })).toBeVisible();
    expect(screen.getByText('Or récupérable', { exact: true })).toBeVisible();
    expect(screen.getByText(/Une vente future ou à délai incertain/)).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Acheter et revendre' })).not.toBeInTheDocument();
  });

  it('keeps saved fixture defaults distinct from the current session and out of browser storage', () => {
    const provider = createFixtureProvider('normal');
    render(<SignalsExperience provider={provider} />);
    fireEvent.click(screen.getByRole('button', { name: 'Adapter ma session' }));
    fireEvent.change(screen.getByLabelText('Durée personnalisée en minutes'), { target: { value: '5' } });
    fireEvent.click(screen.getByRole('button', { name: /Enregistrer comme préférence habituelle/ }));

    expect(screen.getByRole('status')).toHaveTextContent('uniquement en mémoire de démonstration');
    expect(provider.getDefaultPreferences().timeLimitMinutes).toBe(5);
    expect(window.localStorage.length).toBe(0);
    fireEvent.click(screen.getByRole('button', { name: 'Annuler' }));
    expect(screen.getByRole('button', { name: '15 min' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('updates allocation from the draft provider value and explains risk settings without promising protection', () => {
    render(<SignalsExperience provider={createFixtureProvider('normal')} />);
    fireEvent.click(screen.getByRole('button', { name: 'Adapter ma session' }));
    expect(document.querySelector('.p05a-allocation-summary')?.textContent).toContain('90 po');

    fireEvent.click(screen.getByRole('button', { name: /50 %/ }));
    expect(document.querySelector('.p05a-allocation-summary')?.textContent).toContain('150 po');
    expect(document.querySelector('.p05a-allocation-summary')?.textContent).toContain('125 po');

    fireEvent.change(screen.getByLabelText('Pourcentage personnalisé du capital'), { target: { value: '75' } });
    expect(screen.getByRole('status')).toHaveTextContent('Aucun plafond fictif n’est préparé pour cette valeur.');
    expect(screen.getByText(/ne garantit pas une perte maximale/)).toBeVisible();
    expect(screen.getByText(/ne garantit pas le délai d’une offre ou d’une vente/)).toBeVisible();
  });

  it('validates copper inputs exactly without floating-point conversion', () => {
    expect(copperFromMoneyInput('0,5')).toBe('5000');
    expect(copperFromMoneyInput('2.03')).toBe('20300');
    expect(copperFromMoneyInput('-1')).toBeNull();
    expect(copperFromMoneyInput('1.001')).toBeNull();
  });

  it('marks stale account coverage and never offers unsupported activities as selectable', () => {
    render(<SignalsExperience provider={createFixtureProvider('degraded')} />);
    expect(document.querySelector('.p05a-coverage')?.textContent).toContain('Couverture partielle · 2 personnages sur 5');
    fireEvent.click(document.querySelector<HTMLButtonElement>('.p05a-adapt-button')!);
    expect(screen.getByRole('checkbox', { name: /Conversions/ })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: /Recyclage/ })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: /Événements et quotidiennes/ })).toBeDisabled();
  });
});
