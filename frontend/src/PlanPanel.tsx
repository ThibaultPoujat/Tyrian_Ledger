import { useMemo, useState } from 'react';
import MoneyDisplay from './MoneyDisplay';
import { invalidateViewCache, useViewQuery } from './viewQueryCache';

type Money = { copper: string };
type Step = { id: string; action: string; itemName: string; quantity: number; unitPrice: Money | null; state: string };
type Proposal = { id: string; attention: 'Passive' | 'Active'; modeledProfit: Money; committedCapital: Money; interactionSeconds: number; steps: Step[] };
type Plan = Proposal & { state: string; reconciliationState: string; currentStepOrdinal: number; hasUndoableEvent: boolean };
type PlansResponse = { state: 'ready' | 'unavailable'; proposals: Proposal[]; plans: Plan[] };
const headers = { Accept: 'application/json', 'X-Tyrian-Ledger-Request': '1' };

export default function PlanPanel() {
  const query = useViewQuery(useMemo(() => ({ key: 'plans', url: '/api/plans', init: { headers }, validate: isPlansResponse }), []));
  const result = query.data;
  const [mutationError, setMutationError] = useState(false);
  const load = () => query.refresh();
  const post = (path: string, body?: object) => {
    setMutationError(false);
    void fetch(path, { method: 'POST', headers: { ...headers, 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) })
      .then(response => {
        if (!response.ok) throw new Error('plan_mutation_failed');
        invalidateViewCache(['plans', 'recommendations', 'crafting', 'dashboard']);
        return load();
      })
      .catch(() => setMutationError(true));
  };
  if (query.phase === 'loading') return <p aria-live="polite" className="operational-status" role="status">Chargement des plans…</p>;
  if (query.phase === 'error' && result === null) return <p className="operational-status operational-status--error" role="alert">La lecture des plans depuis l'application locale a échoué.</p>;
  if (result?.state !== 'ready') return <section className="signals-zero-state"><div><h2>Plans indisponibles</h2><p>Synchronisez le compte et actualisez les signaux avant de démarrer un plan.</p></div></section>;
  return <section aria-labelledby="plans-title" className="plans-panel"><header className="signals-toolbar"><div><p className="eyebrow">Parcours manuels</p><h2 id="plans-title">Une action à la fois</h2><p>Les ressources d'un plan démarré restent réservées.</p></div><button className="refresh-signals" onClick={load} type="button">Actualiser</button></header>
    {(query.phase === 'refreshing' || query.isStale) && <p className="operational-status" role="status">Plans précédents affichés pendant la vérification de leur validité…</p>}
    {mutationError && <p className="operational-status operational-status--error" role="alert">La modification du plan a échoué. Vérifiez son état actualisé avant d’agir.</p>}
    {result.plans.length === 0 && result.proposals.length === 0 && <div className="signals-zero-state"><div><h3>Aucun plan à démarrer</h3><p>Aucune action compatible ne mérite d'être préparée maintenant.</p></div></div>}
    {result.plans.length > 0 && <ol className="signal-list" aria-label="Plans démarrés">{result.plans.map(plan => <PlanCard key={plan.id} plan={plan} post={post} disabled={query.isStale || query.phase === 'refreshing'} />)}</ol>}
    {result.proposals.length > 0 && <section aria-labelledby="proposal-title"><h3 id="proposal-title">Plans compatibles disponibles</h3><ol className="signal-list">{result.proposals.map(plan => <li className="signal-card" key={plan.id}><article><p className="action-badge">{orientation(plan.attention)}</p><h4>{plan.steps[0]?.itemName ?? 'Plan'}</h4><p>{actionLabel(plan.steps[0]?.action)} · {plan.steps[0]?.quantity} unité{plan.steps[0]?.quantity === 1 ? '' : 's'}</p><p><span>Capital engagé </span><MoneyDisplay compact money={plan.committedCapital} /> <span>· profit modélisé </span><MoneyDisplay compact money={plan.modeledProfit} /></p><button disabled={query.isStale || query.phase === 'refreshing'} onClick={() => post(`/api/plans/${encodeURIComponent(plan.id)}/start`)} type="button">Démarrer</button></article></li>)}</ol></section>}
  </section>;
}

function PlanCard({ plan, post, disabled }: { plan: Plan; post: (path: string, body?: object) => void; disabled: boolean }) {
  const [actualQuantity, setActualQuantity] = useState('');
  const [actualUnitPrice, setActualUnitPrice] = useState('');
  const current = plan.steps.find(step => step.state === 'Current');
  return <li className="signal-card"><article aria-labelledby={`plan-${plan.id}`}><p className="action-badge">{orientation(plan.attention)}</p><h3 id={`plan-${plan.id}`}>{plan.state === 'ReconciliationRequired' ? 'Le plan doit être réconcilié' : 'Plan en cours'}</h3><p role="status">{planStateLabel(plan.state, plan.reconciliationState)}</p><ol>{plan.steps.map(step => <li key={step.id}><strong>{actionLabel(step.action)}</strong> — {step.itemName} · {step.quantity} unité{step.quantity === 1 ? '' : 's'} · {stepStateLabel(step.state)}</li>)}</ol>{current !== undefined && <div><button disabled={disabled} onClick={() => post(`/api/plans/${encodeURIComponent(plan.id)}/complete`, { quantity: current.quantity, unitPriceCopper: current.unitPrice?.copper ?? null })} type="button">Terminé</button><button disabled={disabled} onClick={() => post(`/api/plans/${encodeURIComponent(plan.id)}/complete`, { quantity: 0, unitPriceCopper: null, notPerformed: true })} type="button">Action non effectuée</button><details><summary>Exécution différente</summary><label>Quantité réellement exécutée <input min="1" onChange={event => setActualQuantity(event.target.value)} type="number" value={actualQuantity} /></label><label>Prix unitaire réel en cuivre <input min="0" onChange={event => setActualUnitPrice(event.target.value)} type="number" value={actualUnitPrice} /></label><button disabled={disabled || !/^[1-9]\d*$/.test(actualQuantity) || !/^\d+$/.test(actualUnitPrice)} onClick={() => post(`/api/plans/${encodeURIComponent(plan.id)}/complete`, { quantity: Number(actualQuantity), unitPriceCopper: actualUnitPrice })} type="button">Enregistrer l'exécution réelle</button></details></div>}{plan.hasUndoableEvent && <button disabled={disabled} onClick={() => post(`/api/plans/${encodeURIComponent(plan.id)}/undo`)} type="button">Annuler la dernière étape</button>}</article></li>;
}
function orientation(value: string) { return value === 'Passive' ? 'Passif · attente possible' : 'Actif · enchaînement immédiat'; }
function actionLabel(value: string | undefined) { return ({ BuyNow: 'ACHETER MAINTENANT', PlaceBuyOrder: "PLACER UN ORDRE D’ACHAT", CancelBuyOrder: "ANNULER L’ORDRE D’ACHAT", List: 'METTRE EN VENTE', Relist: 'REMETTRE EN VENTE', SellNow: 'VENDRE MAINTENANT', Craft: 'FABRIQUER' } as Record<string, string>)[value ?? ''] ?? 'Action manuelle'; }
function stepStateLabel(value: string) { return ({ Current: 'Action requise', AwaitingConfirmation: 'En attente d’ArenaNet', PartiallyConfirmed: 'Partiellement confirmé par ArenaNet', Confirmed: 'Confirmé par ArenaNet', RecheckRequired: 'Recalcul requis', Invalidated: 'Étape invalidée', Pending: 'À venir', LocallyReported: 'Déclaré fait' } as Record<string, string>)[value] ?? value; }
function planStateLabel(state: string, reconciliation: string) { return state === 'ReconciliationRequired' || reconciliation === 'Contradicted' ? 'Le plan doit être réconcilié avant toute autre action.' : state === 'Waiting' ? 'Ordre placé : attente de confirmation du marché.' : state === 'Invalid' ? 'Plan annulé : vous pouvez démarrer une proposition mise à jour.' : 'Suivez uniquement l’action mise en avant.'; }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null; }
function isPlansResponse(value: unknown): value is PlansResponse { return isRecord(value) && (value.state === 'ready' || value.state === 'unavailable') && Array.isArray(value.proposals) && Array.isArray(value.plans); }
