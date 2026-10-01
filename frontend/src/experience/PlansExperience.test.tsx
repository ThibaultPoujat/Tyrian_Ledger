import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createFixtureProvider } from './fixtureProvider';
import { SignalsExperience } from './SignalsExperience';
import type { CompletionIntent, CompletionResult, StartResult } from './planModel';

afterEach(cleanup);
const deferred = <T,>() => { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done; }); return { promise, resolve }; };
const reportButton = () => screen.getByRole('button', { name: 'J’ai effectué cette étape' });

describe('comparison and execution through the fixture provider', () => {
  it('selects alternatives and phases without creating an execution; rejects duplicate in-flight starts', async () => {
    const provider = createFixtureProvider('comparison');
    const start = vi.spyOn(provider, 'startPlan');
    const gate = deferred<StartResult>();
    start.mockImplementationOnce(() => gate.promise);
    render(<SignalsExperience provider={provider} initialDestination="plans" />);
    fireEvent.click(screen.getByRole('button', { name: 'Comparer Ventes immédiates' }));
    expect(within(screen.getByLabelText('Récapitulatif du plan sélectionné')).getByRole('heading', { name: 'Ventes immédiates' })).toBeVisible();
    expect(provider.getExecution()).toBeNull();
    const button = screen.getByRole('button', { name: 'Démarrer ce plan' });
    fireEvent.click(button); fireEvent.click(button);
    expect(start).toHaveBeenCalledTimes(1);
    expect(start).toHaveBeenCalledWith('sell-surplus', provider.getDefaultPreferences());
    await act(async () => gate.resolve({ kind: 'conflict', explanation: 'Conflit fictif confirmé.' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Conflit fictif confirmé.');
    expect(provider.getExecution()).toBeNull();
  });

  it('provides a normal observed path and one explicit start, with price/total/fees as read-only instructions', async () => {
    const provider = createFixtureProvider('comparison');
    render(<SignalsExperience provider={provider} initialDestination="plans" />);
    fireEvent.click(screen.getByRole('button', { name: 'Démarrer ce plan' }));
    await screen.findByRole('heading', { name: 'Minerai de mithril' });
    fireEvent.click(screen.getByRole('button', { name: 'Actualiser l’observation' }));
    await screen.findByRole('heading', { name: 'Complément fictif' });
    expect(screen.getByText('Prix unitaire / limite')).toBeVisible();
    expect(screen.getByText('Total instruit')).toBeVisible();
    expect(document.querySelector('.p05b-instruction input')).toBeNull();
    expect(provider.getExecution()?.state).toBe('confirmed');
    expect(screen.getByText('Complément fictif')).toHaveFocus();
  });

  it('filters uncertain sales out of liquid-deadline plans and preserves unavailable reasons', () => {
    const provider = createFixtureProvider('comparison');
    const preferences = { ...provider.getDefaultPreferences(), objective: 'liquid_gold_deadline' as const };
    expect(provider.getComparison(preferences).plans.map(plan => plan.id)).toEqual(['sell-surplus']);
    expect(provider.getComparison({ ...preferences, activities: { trading_post: false, crafting: true } }).unavailable?.code).toBe('preferences');
    expect(createFixtureProvider('insufficient-capital').getComparison(preferences).unavailable?.code).toBe('capital');
    expect(createFixtureProvider('stale').getComparison(preferences).unavailable?.code).toBe('stale');
  });

  it('respects each plan’s active duration at 5/6/7 minutes and the liquid-cash collection deadline', () => {
    const provider = createFixtureProvider('comparison');
    const preferences = provider.getDefaultPreferences();
    for (const minutes of [5, 6]) {
      expect(provider.getComparison({ ...preferences, timeLimitMinutes: minutes }).plans.map(plan => plan.id)).toEqual(['sell-surplus', 'buy-and-resell']);
    }
    expect(provider.getComparison({ ...preferences, timeLimitMinutes: 7 }).plans.map(plan => plan.id)).toEqual(['craft-short-batch', 'sell-surplus', 'buy-and-resell']);
    expect(provider.getComparison({ ...preferences, timeLimitMinutes: 2, objective: 'liquid_gold_deadline' }).plans).toEqual([]);
    expect(provider.getComparison({ ...preferences, timeLimitMinutes: 3, objective: 'liquid_gold_deadline' }).plans.map(plan => plan.id)).toEqual(['sell-surplus']);
    expect(preferences.timeLimitMinutes).toBe(15);
  });

  it.each(['partial', 'contradiction'] as const)('keeps %s quantities, coverage and dependent guidance blocked after recheck and refresh', async scenario => {
    const provider = createFixtureProvider(scenario);
    const before = provider.getExecution()!;
    render(<SignalsExperience provider={provider} initialDestination="plans" />);
    fireEvent.click(screen.getByRole('button', { name: 'Recontrôler l’instruction' }));
    await screen.findByText(/Recontrôle demandé/);
    fireEvent.click(screen.getByRole('button', { name: 'Actualiser l’observation' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Actualiser l’observation' })).toBeEnabled());
    const after = provider.getExecution()!;
    expect(after.currentIndex).toBe(before.currentIndex);
    expect(after.state).toBe(before.state);
    expect(after.completedIds).toEqual(before.completedIds);
    expect(after.reportedIds).toEqual(before.reportedIds);
    expect(after.reportedQuantity).toBe(before.reportedQuantity);
    expect(after.coverageComplete).toBe(false);
    expect(reportButton()).toBeDisabled();
  });

  it('retries a lost acknowledgement only on explicit refresh, with the identical original intent across navigation', async () => {
    const provider = createFixtureProvider('lost-ack');
    const report = vi.spyOn(provider, 'reportPerformed');
    const scope = vi.spyOn(provider, 'getAccountScope');
    render(<SignalsExperience provider={provider} initialDestination="plans" />);
    const button = reportButton();
    fireEvent.click(button); fireEvent.click(button);
    await screen.findByRole('alert');
    expect(report).toHaveBeenCalledTimes(1);
    const intent = report.mock.calls[0][0];
    expect(intent).toMatchObject({ executionId: 'fixture-execution-1', stepId: 'craft-3', expectedRevision: '1', accountScope: 'fixture-account-1' });
    expect(provider.getExecution()?.currentIndex).toBe(3);
    expect(reportButton()).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Signaux' }));
    fireEvent.click(screen.getByRole('button', { name: 'Plans' }));
    fireEvent.click(screen.getByRole('button', { name: 'Actualiser' }));
    await screen.findByRole('heading', { name: 'Composant fictif' });
    expect(report.mock.calls[1][0]).toBe(intent);
    expect(scope.mock.invocationCallOrder[0]).toBeLessThan(report.mock.invocationCallOrder[1]);
    expect(provider.getExecution()?.currentIndex).toBe(3);
    expect(screen.getAllByText(/Déclaré effectué/).length).toBeGreaterThan(0);
    fireEvent.click(reportButton());
    await screen.findByRole('heading', { name: 'Lot d’artisanat fictif' });
    expect(report.mock.calls[2][0].stepId).toBe('craft-4');
    expect(report.mock.calls[2][0].commandId).not.toBe(intent.commandId);
  });

  it('invalidates a pending old-account intent before retry, and ignores delayed old acknowledgements', async () => {
    const provider = createFixtureProvider('active');
    const gate = deferred<CompletionResult>();
    vi.spyOn(provider, 'reportPerformed').mockImplementationOnce(() => gate.promise);
    render(<SignalsExperience provider={provider} initialDestination="plans" />);
    fireEvent.click(reportButton());
    fireEvent.click(screen.getByText('Compte de démonstration'));
    fireEvent.click(screen.getByRole('button', { name: 'Changer de compte fictif' }));
    await act(async () => gate.resolve({ kind: 'acknowledged', commandId: 'old' }));
    expect(screen.queryByRole('heading', { name: 'Lingot de mithril' })).not.toBeInTheDocument();
    expect(provider.getExecution()).toBeNull();
    expect(screen.getByRole('alert')).toHaveTextContent('anciennes déclarations sont invalidées');
  });

  it('checks an externally changed account scope before retrying lost intents', async () => {
    const provider = createFixtureProvider('lost-ack');
    const report = vi.spyOn(provider, 'reportPerformed');
    render(<SignalsExperience provider={provider} initialDestination="plans" />);
    fireEvent.click(reportButton());
    await screen.findByRole('alert');
    provider.switchFixtureAccount();
    fireEvent.click(screen.getByRole('button', { name: 'Actualiser' }));
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('ancienne déclaration est invalidée'));
    expect(report).toHaveBeenCalledTimes(1);
  });

  it('does not restore stale instructions from receipt replay and rejects changed identities', async () => {
    const provider = createFixtureProvider('active');
    const execution = provider.getExecution()!;
    const first: CompletionIntent = { commandId: 'first', executionId: execution.id, stepId: 'craft-3', expectedRevision: execution.revision, accountScope: execution.accountScope };
    await provider.reportPerformed(first);
    const next = provider.getExecution()!;
    await provider.reportPerformed({ ...first, commandId: 'second', stepId: 'craft-4', expectedRevision: next.revision });
    const beforeReplay = provider.getExecution();
    expect((await provider.reportPerformed(first)).kind).toBe('acknowledged');
    expect(provider.getExecution()).toBe(beforeReplay);
    expect((await provider.reportPerformed({ ...first, stepId: 'craft-4' })).kind).toBe('rejected');
    expect(provider.getExecution()?.reportedIds).toEqual(['craft-3', 'craft-4']);
  });

  it.each(['wait', 'partial', 'contradiction'] as const)('renders %s eligibility without inventing continuation', scenario => {
    render(<SignalsExperience provider={createFixtureProvider(scenario)} initialDestination="plans" />);
    expect(reportButton()).toBeDisabled();
    expect(screen.getByText(/fournisseur fictif interdit/)).toBeVisible();
    if (scenario !== 'wait') expect(screen.getByText(/protections non confirmées/)).toBeVisible();
    else expect(screen.getByText(/sans garantie de fraîcheur/)).toBeVisible();
  });

  it('pauses guidance only, resumes the same instruction and undoes a provisional declaration', async () => {
    const provider = createFixtureProvider('active');
    render(<SignalsExperience provider={provider} initialDestination="plans" />);
    fireEvent.click(screen.getByRole('button', { name: 'Mettre le guidage en pause' }));
    await waitFor(() => expect(provider.getExecution()?.state).toBe('paused'));
    expect(screen.getByText(/Aucun ordre en jeu n’a été annulé/)).toBeVisible();
    expect(reportButton()).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Reprendre le guidage' }));
    await waitFor(() => expect(reportButton()).toBeEnabled());
    fireEvent.click(reportButton());
    await screen.findByRole('heading', { name: 'Composant fictif' });
    fireEvent.click(screen.getByRole('button', { name: 'Annuler la déclaration locale' }));
    await screen.findByRole('heading', { name: 'Lingot de mithril' });
    expect(provider.getExecution()?.state).toBe('undone');
  });
});
