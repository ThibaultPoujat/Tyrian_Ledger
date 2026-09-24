import { useMemo, useState } from 'react';
import MoneyDisplay from './MoneyDisplay';
import { invalidateViewCache, useViewQuery } from './viewQueryCache';

type Money = { copper: string };
type Step = { action: string; itemName: string; quantity: number; unitPrice: Money | null };
type Opportunity = { id: string; outputName: string; outputIconUrl: string | null; outputQuantity: number; economics: { netProfit: Money | null; totalCost: Money | null; state: string }; attention: string | null; confidenceBasisPoints?: number; evidenceExplanation?: string[]; interactionSeconds: number | null; isActionable: boolean; exclusions: string[]; procurementExplanation: string[]; planId: string | null; steps: Step[] };
type Response = { state: 'Ready' | 'NoOpportunities' | 'Degraded'; truncationReasons: string[]; summaryExclusions: string[]; opportunities: Opportunity[] };

export default function CraftingPanel() {
  const query = useViewQuery(useMemo(() => ({ key: 'crafting', url: '/api/crafting-opportunities', init: { headers: { 'X-Tyrian-Ledger-Request': '1' } }, validate: isResponse }), []));
  const result = query.data;
  const [mutationError, setMutationError] = useState(false);
  const load = () => query.refresh();
  const start = (id: string) => { setMutationError(false); void fetch(`/api/plans/${encodeURIComponent(id)}/start`, { method: 'POST', headers: { 'X-Tyrian-Ledger-Request': '1' } })
    .then(response => { if (!response.ok) throw new Error('plan_start_failed'); invalidateViewCache(['plans', 'recommendations', 'crafting', 'dashboard']); return load(); }).catch(() => setMutationError(true)); };
  const refreshCrafting = () => { setMutationError(false); void fetch('/api/account-crafting/refresh', { method: 'POST', headers: { 'X-Tyrian-Ledger-Request': '1' } })
    .then(response => { if (!response.ok) throw new Error('crafting_refresh_failed'); invalidateViewCache(['crafting', 'plans', 'recommendations']); return load(); }).catch(() => setMutationError(true)); };
  if (query.phase === 'loading') return <p aria-live="polite" className="operational-status" role="status">Recherche des parcours d’artisanat…</p>;
  if (query.phase === 'error' && result === null) return <section className="signals-zero-state"><div><p className="operational-status operational-status--error" role="alert">L’actualisation ou la recherche d’artisanat depuis l’application locale a échoué.</p><button className="refresh-signals" onClick={refreshCrafting} type="button">Réessayer l’actualisation</button></div></section>;
  if (result === null) return null;
  const data = result;
  const updating = query.phase === 'refreshing' || query.isStale;
  if (data.state === 'Degraded') return <section className="signals-zero-state"><div><h2>Artisanat temporairement indisponible</h2><p>{degraded(data.summaryExclusions)}</p><button className="refresh-signals" onClick={refreshCrafting} type="button">Actualiser les données d’artisanat</button><button className="refresh-signals" onClick={load} type="button">Réessayer</button></div></section>;
  if (data.state === 'NoOpportunities' || data.opportunities.length === 0) return <section className="signals-zero-state"><div><h2>Aucun parcours d’artisanat viable</h2><p>Les coûts, la liquidité ou les données disponibles ne justifient pas une action maintenant.</p>{data.truncationReasons.length > 0 && <p className="operational-status operational-status--warning">Recherche limitée : {data.truncationReasons.map(truncation).join(' ')}</p>}<button className="refresh-signals" onClick={refreshCrafting} type="button">Actualiser les données d’artisanat</button></div></section>;
  return <section aria-labelledby="crafting-workspace-title" className="signals-feed">
    <header className="signals-toolbar"><div><p className="eyebrow">Parcours bornés</p><h2 id="crafting-workspace-title">Actions d’artisanat à considérer</h2><p>Les coûts et la faisabilité sont calculés par l’application locale.</p></div><button className="refresh-signals" onClick={refreshCrafting} type="button">Actualiser les données d’artisanat</button></header>
    {updating && <p className="operational-status" role="status">Parcours précédents affichés pendant la vérification de leur faisabilité…</p>}
    {mutationError && <p className="operational-status operational-status--error" role="alert">L’actualisation a échoué. Les actions restent désactivées jusqu’à une vérification réussie.</p>}
    {data.truncationReasons.length > 0 && <p className="operational-status operational-status--warning" role="status">Recherche limitée : {data.truncationReasons.map(truncation).join(' ')}</p>}
    <ol className="signal-list" aria-label="Opportunités d’artisanat">{data.opportunities.map(opportunity => <li className="signal-card" key={opportunity.id}><article>
      <div className="crafting-card-heading">{opportunity.outputIconUrl && <img alt="" src={opportunity.outputIconUrl} />}<div><p className="action-badge">{opportunity.attention === 'Passive' ? 'PASSIF · ORDRES À CONFIRMER' : 'ACTIF · FABRICATION IMMÉDIATE'}</p><h3>{opportunity.outputName}</h3></div></div>
      <p><strong>Quantité à fabriquer :</strong> {opportunity.outputQuantity}</p>
      {opportunity.economics.netProfit && <p><span>Profit modélisé </span><MoneyDisplay compact money={opportunity.economics.netProfit} /> <span>· capital engagé </span>{opportunity.economics.totalCost && <MoneyDisplay compact money={opportunity.economics.totalCost} />}</p>}
      <p>Confiance des données : {confidence(opportunity.confidenceBasisPoints ?? 0)}.</p>
      <p>Temps d’interaction estimé : {Math.max(1, Math.ceil((opportunity.interactionSeconds ?? 0) / 60))} min.</p>
      {opportunity.isActionable && opportunity.planId ? <button disabled={updating || mutationError} onClick={() => start(opportunity.planId!)} type="button">Démarrer ce plan</button> : <p className="operational-status operational-status--warning">Non actionnable : {opportunity.exclusions.map(exclusion).join(', ')}</p>}
      <details><summary>Pourquoi ?</summary><h4>Approvisionnement et faisabilité</h4><ul>{[...(opportunity.evidenceExplanation ?? []), ...opportunity.procurementExplanation].length > 0 ? [...(opportunity.evidenceExplanation ?? []), ...opportunity.procurementExplanation].map((line, index) => <li key={index}>{line}</li>) : <li>Les prix, frais et profondeurs d’exécution ont été vérifiés avant cette proposition.</li>}</ul><ol>{opportunity.steps.map((step, index) => <li key={`${step.action}-${index}`}>{action(step.action)} — {step.itemName} · {step.quantity} unité{step.quantity === 1 ? '' : 's'}</li>)}</ol></details>
    </article></li>)}</ol>
  </section>;
}

function action(value: string) { return ({ BuyNow: 'Acheter maintenant', PlaceBuyOrder: 'Placer un ordre d’achat', Craft: 'Fabriquer', List: 'Mettre en vente' } as Record<string, string>)[value] ?? 'Action manuelle'; }
function truncation(value: string) { return ({ RecipeLimit: 'nombre de recettes atteint.', DepthLimit: 'profondeur de recettes atteinte.', CandidateLimit: 'nombre de propositions atteint.', WorkLimit: 'budget de calcul atteint.', MarketDataLimit: 'nombre de données de marché atteint.' } as Record<string, string>)[value] ?? 'limite atteinte.'; }
function degraded(reasons: string[]) { return reasons.includes('CapabilityUnavailable') ? 'Les disciplines, niveaux ou recettes vérifiés ne sont pas disponibles.' : reasons.includes('StaleEvidence') ? 'Les données du compte ou du marché sont trop anciennes pour proposer un parcours sûr.' : 'Les données nécessaires pour évaluer les coûts ou le marché sont incomplètes.'; }
function exclusion(value: string) { return ({ InsufficientOutputDepth: 'profondeur de vente insuffisante', InsufficientInputDepth: 'profondeur d’achat insuffisante', WeakHistory: 'historique insuffisant', StaleEvidence: 'données périmées', MissingInputEvidence: 'coût inconnu', ResourceConflict: 'ressource réservée par un autre plan', NotProfitable: 'profit non justifié', CycleDetected: 'cycle de recettes détecté' } as Record<string, string>)[value] ?? 'données incomplètes'; }
function confidence(value: number) { return value >= 8_000 ? 'élevée' : value >= 6_000 ? 'modérée' : 'limitée'; }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null; }
function isResponse(value: unknown): value is Response { return isRecord(value) && (value.state === 'Ready' || value.state === 'NoOpportunities' || value.state === 'Degraded') && Array.isArray(value.opportunities) && Array.isArray(value.truncationReasons) && Array.isArray(value.summaryExclusions); }
