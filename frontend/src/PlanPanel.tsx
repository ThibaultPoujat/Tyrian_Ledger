import { useCallback, useMemo, useRef, useState } from 'react';
import MoneyDisplay from './MoneyDisplay';
import { invalidateViewCache, useViewQuery } from './viewQueryCache';

type Money = { copper: string };
type Step = { id: string; action: string; itemName: string; quantity: number; unitPrice: Money | null; state: string };
type Proposal = { id: string; attention: 'Passive' | 'Active'; modeledProfit: Money; committedCapital: Money; interactionSeconds: number; holdingsExplanation?: string[]; steps: Step[] };
type Plan = Proposal & { state: string; reconciliationState: string; currentStepOrdinal: number; revision: string; hasUndoableEvent: boolean; holdingsEligibilityReason?: string | null };
type PlanSelection = { recommendationCandidates: number; generatedCandidates: number; rejectedHardConstraints: number; rejectedResourceConflicts: number; rejectedSelectionConstraints: number; rejectedDecisionSafetyGate?: number; selected: number; reason: string; reusedDecision: boolean };
type PlansResponse = { state: 'ready' | 'unavailable'; proposals: Proposal[]; plans: Plan[]; selection?: PlanSelection; accountCacheScope?: string | null };
type Operation = 'ReportPerformed' | 'NotPerformed';
type PendingCompletion = {
  planId: string;
  stepId: string;
  expectedRevision: string;
  commandId: string;
  operation: Operation;
  quantity: number;
  unitPriceCopper: string | null;
  accountViewScope: string;
  sequence: number;
  status: 'in-flight' | 'unknown';
};
const headers = { Accept: 'application/json', 'X-Tyrian-Ledger-Request': '1' };
const completionScopeHeader = 'X-Tyrian-Ledger-Account-View-Scope';
let fallbackCommandSequence = 0;

export default function PlanPanel() {
  const query = useViewQuery(useMemo(() => ({
    key: 'plans', url: '/api/plans', init: { headers }, validate: isPlansResponse,
    scopeFrom: (value: PlansResponse) => value.accountCacheScope,
  }), []));
  const result = query.data;
  const pendingRef = useRef<PendingCompletion[]>([]);
  const nextSequence = useRef(0);
  const inFlight = useRef(new Set<string>());
  const recoveryCycle = useRef<Promise<void> | null>(null);
  const [pending, setPending] = useState<PendingCompletion[]>([]);
  const [mutationError, setMutationError] = useState(false);
  const [recoveryBusy, setRecoveryBusy] = useState(false);

  const updatePending = useCallback((update: (current: PendingCompletion[]) => PendingCompletion[]) => {
    const next = update(pendingRef.current);
    pendingRef.current = next;
    setPending(next);
  }, []);

  const transmit = useCallback(async (command: PendingCompletion) => {
    if (inFlight.current.has(command.commandId)) return { kind: 'in-flight' as const, scopeChanged: false };
    inFlight.current.add(command.commandId);
    updatePending(current => current.map(item => item.commandId === command.commandId ? { ...item, status: 'in-flight' } : item));
    try {
      const response = await fetch(`/api/plans/${encodeURIComponent(command.planId)}/complete`, {
        method: 'POST',
        headers: { ...headers, 'Content-Type': 'application/json', [completionScopeHeader]: command.accountViewScope },
        body: JSON.stringify({
          stepId: command.stepId,
          expectedRevision: command.expectedRevision,
          commandId: command.commandId,
          operation: command.operation,
          quantity: command.quantity,
          unitPriceCopper: command.unitPriceCopper,
        }),
      });
      if (response.ok) {
        updatePending(current => current.filter(item => item.commandId !== command.commandId));
        return { kind: 'acknowledged' as const, scopeChanged: false };
      }

      let errorCode = '';
      try {
        const body: unknown = await response.json();
        if (isRecord(body) && typeof body.error === 'string') errorCode = body.error;
      } catch { /* The non-success status still definitively rejects a command. */ }
      if (response.status >= 500) {
        updatePending(current => current.map(item => item.commandId === command.commandId ? { ...item, status: 'unknown' } : item));
        setMutationError(true);
        return { kind: 'unknown' as const, scopeChanged: false };
      }

      updatePending(current => current.filter(item => item.commandId !== command.commandId));
      setMutationError(true);
      return { kind: 'rejected' as const, scopeChanged: errorCode === 'account_scope_changed' };
    } catch {
      updatePending(current => current.map(item => item.commandId === command.commandId ? { ...item, status: 'unknown' } : item));
      setMutationError(true);
      return { kind: 'unknown' as const, scopeChanged: false };
    } finally {
      inFlight.current.delete(command.commandId);
    }
  }, [updatePending]);

  const finishAcknowledgedMutation = useCallback(() => {
    setMutationError(false);
    invalidateViewCache(['plans', 'recommendations', 'crafting', 'dashboard', 'calculation-explanations']);
    void query.refresh();
  }, [query.refresh]);

  const complete = useCallback((plan: Plan, step: Step, operation: Operation, quantity: number, unitPriceCopper: string | null) => {
    const accountViewScope = result?.accountCacheScope;
    if (!accountViewScope) { setMutationError(true); return; }
    const identity = { planId: plan.id, stepId: step.id, expectedRevision: plan.revision, operation, quantity, unitPriceCopper, accountViewScope };
    const existing = pendingRef.current.find(item =>
      item.planId === identity.planId && item.stepId === identity.stepId && item.expectedRevision === identity.expectedRevision &&
      item.operation === identity.operation && item.quantity === identity.quantity && item.unitPriceCopper === identity.unitPriceCopper &&
      item.accountViewScope === identity.accountViewScope);
    if (existing?.status === 'in-flight') return;

    const command = existing ?? {
      ...identity,
      commandId: createCommandId(),
      sequence: nextSequence.current++,
      status: 'unknown' as const,
    };
    if (!existing) updatePending(current => [...current, command]);
    setMutationError(false);
    void transmit(command).then(outcome => {
      if (outcome.kind === 'acknowledged') finishAcknowledgedMutation();
    });
  }, [finishAcknowledgedMutation, result?.accountCacheScope, transmit, updatePending]);

  const post = (path: string, revision?: string) => {
    setMutationError(false);
    if (revision !== undefined && !result?.accountCacheScope) { setMutationError(true); return; }
    void fetch(path, { method: 'POST', headers: revision === undefined ? headers : { ...headers, 'Content-Type': 'application/json', [completionScopeHeader]: result!.accountCacheScope! },
      body: revision === undefined ? undefined : JSON.stringify({ expectedRevision: revision }) })
      .then(response => {
        if (!response.ok) throw new Error('plan_mutation_failed');
        invalidateViewCache(['plans', 'recommendations', 'crafting', 'dashboard', 'calculation-explanations']);
        return query.refresh();
      })
      .catch(() => setMutationError(true));
  };

  const readFreshScope = async (): Promise<string | null> => {
    try {
      const response = await fetch('/api/plans/context', { headers: { ...headers, 'Cache-Control': 'no-cache' } });
      const payload: unknown = await response.json();
      if (!response.ok || !isRecord(payload) || payload.state !== 'ready' || typeof payload.accountCacheScope !== 'string' || !payload.accountCacheScope)
        return null;
      return payload.accountCacheScope;
    } catch {
      return null;
    }
  };

  const load = useCallback(() => {
    if (recoveryCycle.current) return recoveryCycle.current;
    const cycle = (async () => {
      const snapshot = pendingRef.current.filter(item => item.status === 'unknown').sort((left, right) => left.sequence - right.sequence);
      if (snapshot.length === 0) { await query.refresh(); return; }
      setRecoveryBusy(true);
      try {
        const currentScope = await readFreshScope();
        if (!currentScope) {
          setMutationError(true);
          await query.refresh();
          return;
        }

        const oldScopes = new Set(snapshot.filter(item => item.accountViewScope !== currentScope).map(item => item.accountViewScope));
        if (oldScopes.size > 0) {
          updatePending(current => current.filter(item => !oldScopes.has(item.accountViewScope)));
        }

        const matching = snapshot.filter(item => item.accountViewScope === currentScope);
        for (const item of matching) {
          if (!pendingRef.current.some(current => current.commandId === item.commandId && current.status === 'unknown')) continue;
          const outcome = await transmit(item);
          if (outcome.kind === 'acknowledged') setMutationError(false);
          if (outcome.scopeChanged) {
            updatePending(current => current.filter(entry => entry.accountViewScope !== item.accountViewScope));
            setMutationError(true);
            await readFreshScope();
            break;
          }
        }
        await query.refresh();
      } finally {
        setRecoveryBusy(false);
        recoveryCycle.current = null;
      }
    })();
    recoveryCycle.current = cycle;
    return cycle;
  }, [query.refresh, transmit, updatePending]);

  if (query.phase === 'loading') return <section className="operational-status"><p aria-live="polite" role="status">Chargement des plans…</p>{pending.length > 0 && <button className="refresh-signals" onClick={() => void load()} type="button">Actualiser</button>}</section>;
  if (query.phase === 'error' && result === null) return <section className="operational-status operational-status--error"><p role="alert">La lecture des plans depuis l'application locale a échoué.</p>{pending.length > 0 && <button className="refresh-signals" onClick={() => void load()} type="button">Actualiser</button>}</section>;
  if (result?.state !== 'ready') return <section className="signals-zero-state"><div><h2>Plans indisponibles</h2><p>Synchronisez le compte et actualisez les signaux avant de démarrer un plan.</p>{pending.length > 0 && <button className="refresh-signals" onClick={() => void load()} type="button">Actualiser</button>}</div></section>;
  return <section aria-labelledby="plans-title" className="plans-panel"><header className="signals-toolbar"><div><p className="eyebrow">Parcours manuels</p><h2 id="plans-title">Une action à la fois</h2><p>Les ressources d'un plan démarré restent réservées.</p></div><button className="refresh-signals" onClick={() => void load()} type="button">{recoveryBusy ? 'Actualisation en cours…' : 'Actualiser'}</button></header>
    {(query.phase === 'refreshing' || query.isStale) && <p className="operational-status" role="status">Plans précédents affichés pendant la vérification de leur validité…</p>}
    {mutationError && <p className="operational-status operational-status--error" role="alert">La modification du plan a échoué. Vérifiez son état actualisé avant d’agir.</p>}
    {result.plans.length === 0 && result.proposals.length === 0 && <PlanEmptyState selection={result.selection} />}
    {result.plans.length > 0 && <ol className="signal-list" aria-label="Plans démarrés">{result.plans.map(plan => <PlanCard key={plan.id} plan={plan} post={post} complete={complete} disabled={query.isStale || query.phase === 'refreshing'} />)}</ol>}
    {result.proposals.length > 0 && <section aria-labelledby="proposal-title"><h3 id="proposal-title">Plans compatibles disponibles</h3><ol className="signal-list">{result.proposals.map(plan => <li className="signal-card" key={plan.id}><article><p className="action-badge">{orientation(plan.attention)}</p><h4>{plan.steps[0]?.itemName ?? 'Plan'}</h4><p>{actionLabel(plan.steps[0]?.action)} · {plan.steps[0]?.quantity} unité{plan.steps[0]?.quantity === 1 ? '' : 's'}</p>{plan.holdingsExplanation?.map(line => <p key={line}>{line}</p>)}<p><span>Capital engagé </span><MoneyDisplay compact money={plan.committedCapital} /> <span>· profit modélisé </span><MoneyDisplay compact money={plan.modeledProfit} /></p><button disabled={query.isStale || query.phase === 'refreshing'} onClick={() => post(`/api/plans/${encodeURIComponent(plan.id)}/start`)} type="button">Démarrer</button></article></li>)}</ol></section>}
  </section>;
}

function PlanEmptyState({ selection }: { selection: PlanSelection | undefined }) {
  const safetyGate = selection?.rejectedDecisionSafetyGate ?? 0;
  return <div className="signals-zero-state"><div><h3>Aucun plan à démarrer</h3><p>{planEmptyReason(selection?.reason)}</p>{selection && <p className="operational-status" role="status">{safetyGate > 0 ? `${selection.recommendationCandidates} signal${selection.recommendationCandidates === 1 ? '' : 'ux'} ont produit ${selection.generatedCandidates} candidat${selection.generatedCandidates === 1 ? '' : 's'}, mais ${safetyGate} candidat${safetyGate === 1 ? '' : 's'} ne ${safetyGate === 1 ? 'peut' : 'peuvent'} pas être exposé${safetyGate === 1 ? '' : 's'} tant que le dimensionnement du portefeuille n’est pas vérifié.` : `${selection.recommendationCandidates} signal${selection.recommendationCandidates === 1 ? '' : 'ux'} transformé${selection.recommendationCandidates === 1 ? '' : 's'} en candidat${selection.generatedCandidates === 1 ? '' : 's'} · ${selection.rejectedHardConstraints} rejeté${selection.rejectedHardConstraints === 1 ? '' : 's'} par les contraintes strictes · ${selection.rejectedResourceConflicts} bloqué${selection.rejectedResourceConflicts === 1 ? '' : 's'} par les ressources ou réservations · ${selection.rejectedSelectionConstraints} écarté${selection.rejectedSelectionConstraints === 1 ? '' : 's'} à la sélection.`}</p>}</div></div>;
}

function PlanCard({ plan, post, complete, disabled }: { plan: Plan; post: (path: string, revision?: string) => void; complete: (plan: Plan, step: Step, operation: Operation, quantity: number, unitPriceCopper: string | null) => void; disabled: boolean }) {
  const [actualQuantity, setActualQuantity] = useState('');
  const [actualUnitPrice, setActualUnitPrice] = useState('');
  const current = plan.steps.find(step => step.state === 'Current');
  const validActualQuantity = /^[1-9]\d*$/.test(actualQuantity) && Number.isSafeInteger(Number(actualQuantity)) && Number(actualQuantity) <= 2_147_483_647;
  return <li className="signal-card"><article aria-labelledby={`plan-${plan.id}`}><p className="action-badge">{orientation(plan.attention)}</p><h3 id={`plan-${plan.id}`}>{plan.state === 'ReconciliationRequired' ? 'Le plan doit être réconcilié' : 'Plan en cours'}</h3><p role="status">{plan.holdingsEligibilityReason ? "Les ressources, leur localisation ou le personnage requis doivent être vérifiés avant de poursuivre. Actualisez les preuves du compte." : planStateLabel(plan.state, plan.reconciliationState)}</p>{plan.holdingsExplanation?.map(line => <p key={line}>{line}</p>)}<ol>{plan.steps.map(step => <li key={step.id}><strong>{actionLabel(step.action)}</strong> — {step.itemName} · {step.quantity} unité{step.quantity === 1 ? '' : 's'} · {stepStateLabel(step.state)}</li>)}</ol><button className="calculation-link" onClick={() => window.dispatchEvent(new CustomEvent('tyrian-ledger:open-calculation', { detail: plan.id }))} type="button">Pourquoi ? Voir le calcul</button>{current !== undefined && <div><button disabled={disabled} onClick={() => complete(plan, current, 'ReportPerformed', current.quantity, current.unitPrice?.copper ?? null)} type="button">Terminé</button><button disabled={disabled} onClick={() => complete(plan, current, 'NotPerformed', 0, null)} type="button">Action non effectuée</button><details><summary>Exécution différente</summary><label>Quantité réellement exécutée <input min="1" onChange={event => setActualQuantity(event.target.value)} type="number" value={actualQuantity} /></label><label>Prix unitaire réel en cuivre <input min="0" onChange={event => setActualUnitPrice(event.target.value)} type="number" value={actualUnitPrice} /></label><button disabled={disabled || !validActualQuantity || !/^\d+$/.test(actualUnitPrice) || !Number.isSafeInteger(Number(actualUnitPrice))} onClick={() => complete(plan, current, 'ReportPerformed', Number(actualQuantity), actualUnitPrice)} type="button">Enregistrer l'exécution réelle</button></details></div>}{plan.hasUndoableEvent && <button disabled={disabled} onClick={() => post(`/api/plans/${encodeURIComponent(plan.id)}/undo`, plan.revision)} type="button">Annuler la dernière étape</button>}</article></li>;
}
function orientation(value: string) { return value === 'Passive' ? 'Passif · attente possible' : 'Actif · enchaînement immédiat'; }
function actionLabel(value: string | undefined) { return ({ BuyNow: 'ACHETER MAINTENANT', PlaceBuyOrder: "PLACER UN ORDRE D’ACHAT", CancelBuyOrder: "ANNULER L’ORDRE D’ACHAT", List: 'METTRE EN VENTE', Relist: 'REMETTRE EN VENTE', SellNow: 'VENDRE MAINTENANT', Craft: 'FABRIQUER' } as Record<string, string>)[value ?? ''] ?? 'Action manuelle'; }
function stepStateLabel(value: string) { return ({ Current: 'Action requise', AwaitingConfirmation: 'En attente d’ArenaNet', PartiallyConfirmed: 'Partiellement confirmé par ArenaNet', Confirmed: 'Confirmé par ArenaNet', RecheckRequired: 'Recalcul requis', Invalidated: 'Étape invalidée', Pending: 'À venir', LocallyReported: 'Déclaré fait' } as Record<string, string>)[value] ?? value; }
function planStateLabel(state: string, reconciliation: string) { return state === 'ReconciliationRequired' || reconciliation === 'Contradicted' ? 'Le plan doit être réconcilié avant toute autre action.' : state === 'Waiting' ? 'Ordre placé : attente de confirmation du marché.' : state === 'Invalid' ? 'Plan annulé : vous pouvez démarrer une proposition mise à jour.' : 'Suivez uniquement l’action mise en avant.'; }
function planEmptyReason(reason: string | undefined) { return ({ account_evidence_unavailable: 'Les preuves du compte ne sont pas disponibles : aucun plan ne peut être préparé.', recommendations_not_ready: 'Les signaux ne sont pas suffisamment vérifiés pour préparer un plan.', buy_sizing_unavailable: 'Le dimensionnement sûr du portefeuille est indisponible, notamment lorsqu’un coût d’acquisition de vente reste inconnu. Aucun signal n’est donc exécutable.', portfolio_sizing_unavailable: 'Le dimensionnement sûr du portefeuille est indisponible : aucun signal n’est exécutable.', hard_constraints: 'Les candidats ne satisfont pas les contraintes strictes de sûreté.', resource_conflicts: 'Les ressources vérifiées ou réservées ne permettent aucun plan compatible.', cash_reserve: 'La réserve de cuivre vérifiée doit rester intacte.', selection_constraints: 'Les contraintes de sélection ne permettent aucune combinaison sûre.' } as Record<string, string>)[reason ?? ''] ?? "Aucune action compatible ne mérite d'être préparée maintenant."; }
function createCommandId() { return globalThis.crypto?.randomUUID?.() ?? `plan-command-${Date.now().toString(36)}-${(++fallbackCommandSequence).toString(36)}`; }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null; }
function isPlansResponse(value: unknown): value is PlansResponse { return isRecord(value) && (value.state === 'ready' || value.state === 'unavailable') && Array.isArray(value.proposals) && Array.isArray(value.plans); }
