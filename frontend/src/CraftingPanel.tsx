import { useMemo, useState } from 'react';
import MoneyDisplay from './MoneyDisplay';
import { invalidateViewCache, useViewQuery } from './viewQueryCache';

type Money = { copper: string };
type Step = { action: string; itemName: string; quantity: number; unitPrice: Money | null };
type Opportunity = { id: string; outputName: string; outputIconUrl: string | null; outputQuantity: number; economics: { netProfit: Money | null; totalCost: Money | null; state: string }; attention: string | null; confidenceBasisPoints?: number; evidenceExplanation?: string[]; interactionSeconds: number | null; isActionable: boolean; exclusions: string[]; procurementExplanation: string[]; planId: string | null; steps: Step[] };
type Response = { state: 'Ready' | 'NoOpportunities' | 'Degraded'; truncationReasons: string[]; summaryExclusions: string[]; evidenceFailureCode?: string | null; opportunities: Opportunity[] };
type CraftingRefreshResponse = {
  outcome: 'succeeded' | 'failed';
  changed: boolean | null;
  capturedAtUtc: string | null;
  error: string | null;
  bank: CraftingFeature | null;
  materials: CraftingFeature | null;
  recipes: CraftingFeature | null;
  crafting: CraftingFeature | null;
};
type CraftingFeature = { availability: string; count: number | null; error: string | null };

export default function CraftingPanel() {
  const query = useViewQuery(useMemo(() => ({ key: 'crafting', url: '/api/crafting-opportunities', init: { headers: { 'X-Tyrian-Ledger-Request': '1' } }, validate: isResponse }), []));
  const result = query.data;
  const [mutationError, setMutationError] = useState(false);
  const [refreshMessage, setRefreshMessage] = useState<string | null>(null);
  const [refreshingCrafting, setRefreshingCrafting] = useState(false);
  const [retryingSearch, setRetryingSearch] = useState(false);
  const load = () => query.refresh();
  const start = (id: string) => { setMutationError(false); void fetch(`/api/plans/${encodeURIComponent(id)}/start`, { method: 'POST', headers: { 'X-Tyrian-Ledger-Request': '1' } })
    .then(response => { if (!response.ok) throw new Error('plan_start_failed'); invalidateViewCache(['plans', 'recommendations', 'crafting', 'dashboard']); return load(); }).catch(() => setMutationError(true)); };
  const refreshCrafting = () => {
    setMutationError(false);
    setRefreshingCrafting(true);
    setRefreshMessage("Actualisation des données d’artisanat en cours…");
    void fetch('/api/account-crafting/refresh', { method: 'POST', headers: { 'X-Tyrian-Ledger-Request': '1' } })
      .then(async response => {
        const payload: unknown = await response.json();
        if (!isCraftingRefreshResponse(payload)) throw new Error('crafting_refresh_invalid');
        if (!response.ok || payload.outcome !== 'succeeded') throw new Error(payload.error ?? 'crafting_refresh_failed');
        setRefreshMessage(craftingRefreshSuccessMessage(payload));
        invalidateViewCache(['crafting', 'plans', 'recommendations']);
        return load();
      })
      .then(success => {
        if (!success) {
          setMutationError(true);
          setRefreshMessage("La recherche d’artisanat a échoué après l’actualisation. Les résultats précédents restent visibles.");
        }
      })
      .catch(error => {
        setMutationError(true);
        setRefreshMessage(craftingRefreshFailureMessage(error));
      })
      .finally(() => setRefreshingCrafting(false));
  };
  const retrySearch = () => {
    setRetryingSearch(true);
    setRefreshMessage("Recherche des parcours d’artisanat relancée…");
    void load().then(success => {
      setRefreshMessage(success
        ? "Recherche terminée. L’état affiché reflète les dernières preuves vérifiées."
        : "La recherche d’artisanat a échoué. Les résultats précédents restent visibles.");
    }).finally(() => setRetryingSearch(false));
  };
  const progress = (query.phase === 'refreshing' || query.isStale) && !refreshingCrafting && !retryingSearch;
  const refreshStatus = refreshMessage && <p className={mutationError ? 'operational-status operational-status--error' : 'operational-status'} role={mutationError ? 'alert' : 'status'}>{refreshMessage}</p>;
  if (query.phase === 'loading') return <p aria-live="polite" className="operational-status" role="status">Recherche des parcours d’artisanat…</p>;
  if (query.phase === 'error' && result === null) return <section className="signals-zero-state"><div><p className="operational-status operational-status--error" role="alert">L’actualisation ou la recherche d’artisanat depuis l’application locale a échoué.</p>{refreshStatus}<button className="refresh-signals" disabled={refreshingCrafting} onClick={refreshCrafting} type="button">{refreshingCrafting ? 'Actualisation en cours…' : 'Réessayer l’actualisation'}</button></div></section>;
  if (result === null) return null;
  const data = result;
  const updating = query.phase === 'refreshing' || query.isStale || refreshingCrafting;
  if (data.state === 'Degraded') return <section className="signals-zero-state"><div><h2>Artisanat temporairement indisponible</h2><p>{degraded(data.summaryExclusions, data.evidenceFailureCode)}</p>{progress && <p className="operational-status" role="status">Parcours précédents affichés pendant la vérification de leur faisabilité…</p>}{refreshStatus}<button className="refresh-signals" disabled={refreshingCrafting} onClick={refreshCrafting} type="button">{refreshingCrafting ? 'Actualisation en cours…' : 'Actualiser les données d’artisanat'}</button><button className="refresh-signals" disabled={retryingSearch} onClick={retrySearch} type="button">{retryingSearch ? 'Recherche en cours…' : 'Réessayer'}</button></div></section>;
  if (data.state === 'NoOpportunities' || data.opportunities.length === 0) return <section className="signals-zero-state"><div><h2>Aucun parcours d’artisanat viable</h2><p>{noOpportunityExplanation(data.summaryExclusions)}</p>{progress && <p className="operational-status" role="status">Parcours précédents affichés pendant la vérification de leur faisabilité…</p>}{refreshStatus}{data.truncationReasons.length > 0 && <p className="operational-status operational-status--warning">Recherche limitée : {data.truncationReasons.map(truncation).join(' ')}</p>}<button className="refresh-signals" disabled={refreshingCrafting} onClick={refreshCrafting} type="button">{refreshingCrafting ? 'Actualisation en cours…' : 'Actualiser les données d’artisanat'}</button></div></section>;
  return <section aria-labelledby="crafting-workspace-title" className="signals-feed">
    <header className="signals-toolbar"><div><p className="eyebrow">Parcours bornés</p><h2 id="crafting-workspace-title">Actions d’artisanat à considérer</h2><p>Les coûts et la faisabilité sont calculés par l’application locale.</p></div><button className="refresh-signals" disabled={refreshingCrafting} onClick={refreshCrafting} type="button">{refreshingCrafting ? 'Actualisation en cours…' : 'Actualiser les données d’artisanat'}</button></header>
    {updating && <p className="operational-status" role="status">Parcours précédents affichés pendant la vérification de leur faisabilité…</p>}
    {refreshStatus}
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
function degraded(reasons: string[], failure?: string | null) { return reasons.includes('CapabilityUnavailable') ? 'Les disciplines, niveaux ou recettes vérifiés ne sont pas disponibles.' : reasons.includes('StaleEvidence') ? 'Les données du compte ou du marché sont trop anciennes pour proposer un parcours sûr.' : failure === 'recipe_definitions_unavailable' ? 'Les définitions des recettes déverrouillées ne sont pas disponibles : les coûts des ingrédients ne peuvent pas être calculés.' : failure === 'market_listings_unavailable' ? 'Les prix ou profondeurs de marché nécessaires aux ingrédients ou aux sorties sont indisponibles : aucun profit n’est supposé.' : failure === 'market_metadata_unavailable' ? 'Les métadonnées d’au moins un ingrédient ou produit sont indisponibles : aucun parcours incomplet n’est proposé.' : failure === 'crafting_search_failed' ? 'La recherche d’artisanat a échoué avant de vérifier les coûts. Réessayez ; aucun parcours incomplet n’est proposé.' : 'Les données nécessaires pour évaluer les coûts ou le marché sont incomplètes.'; }
function noOpportunityExplanation(reasons: string[]) { return reasons.includes('MissingInputEvidence') ? 'Certaines recettes n’ont pas toutes les preuves de marché nécessaires pour leurs ingrédients ou leur sortie. Elles restent exclues : aucun prix, profondeur ou profit n’est inventé.' : 'Les coûts, la liquidité ou les données disponibles ne justifient pas une action maintenant.'; }
function exclusion(value: string) { return ({ InsufficientOutputDepth: 'profondeur de vente insuffisante', InsufficientInputDepth: 'profondeur d’achat insuffisante', WeakHistory: 'historique insuffisant', StaleEvidence: 'données périmées', MissingInputEvidence: 'coût inconnu', ResourceConflict: 'ressource réservée par un autre plan', NotProfitable: 'profit non justifié', CycleDetected: 'cycle de recettes détecté' } as Record<string, string>)[value] ?? 'données incomplètes'; }
function confidence(value: number) { return value >= 8_000 ? 'élevée' : value >= 6_000 ? 'modérée' : 'limitée'; }
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null; }
function isResponse(value: unknown): value is Response { return isRecord(value) && (value.state === 'Ready' || value.state === 'NoOpportunities' || value.state === 'Degraded') && Array.isArray(value.opportunities) && Array.isArray(value.truncationReasons) && Array.isArray(value.summaryExclusions); }
function isCraftingFeature(value: unknown): value is CraftingFeature { return isRecord(value) && typeof value.availability === 'string' && (typeof value.count === 'number' || value.count === null) && (typeof value.error === 'string' || value.error === null); }
function isCraftingRefreshResponse(value: unknown): value is CraftingRefreshResponse { return isRecord(value) && (value.outcome === 'succeeded' || value.outcome === 'failed') && (typeof value.changed === 'boolean' || value.changed === null) && (typeof value.capturedAtUtc === 'string' || value.capturedAtUtc === null) && (typeof value.error === 'string' || value.error === null) && (value.bank === null || isCraftingFeature(value.bank)) && (value.materials === null || isCraftingFeature(value.materials)) && (value.recipes === null || isCraftingFeature(value.recipes)) && (value.crafting === null || isCraftingFeature(value.crafting)); }
function craftingRefreshSuccessMessage(value: CraftingRefreshResponse) { const unavailable = [value.bank, value.materials, value.recipes, value.crafting].filter(feature => feature !== null && feature.availability !== 'Available'); if (value.changed === false) return unavailable.length === 0 ? "Vérification terminée : les données d’artisanat sont inchangées." : "Vérification terminée : les données sont inchangées, mais certaines preuves restent indisponibles."; return unavailable.length === 0 ? "Données d’artisanat actualisées. La faisabilité des parcours est recalculée." : "Données d’artisanat actualisées, mais certaines preuves restent indisponibles. Aucun parcours incomplet n’est proposé."; }
function craftingRefreshFailureMessage(error: unknown) { const code = error instanceof Error ? error.message : ''; return ({ credential_unavailable: "La clé ArenaNet n’est pas accessible dans le coffre du système.", credential_not_configured: "Aucune clé ArenaNet n’est configurée.", unauthorized: 'La clé ArenaNet a été refusée ou révoquée.', forbidden: 'La clé ArenaNet ne possède pas les autorisations nécessaires pour l’artisanat.', rate_limited: 'ArenaNet limite temporairement l’actualisation. Réessayez plus tard.', upstream_unavailable: 'ArenaNet est temporairement indisponible. Les résultats précédents restent visibles.', transport_failure: 'La requête vers ArenaNet a échoué ou a expiré. Les résultats précédents restent visibles.', invalid_payload: 'ArenaNet a renvoyé des données d’artisanat inattendues. Les résultats précédents restent visibles.' } as Record<string, string>)[code] ?? "L’actualisation des données d’artisanat a échoué. Les résultats précédents restent visibles."; }
