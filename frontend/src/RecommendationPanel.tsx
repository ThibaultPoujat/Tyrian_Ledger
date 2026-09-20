import { useEffect, useMemo, useRef, useState } from 'react';
import MoneyDisplay from './MoneyDisplay';

type Money = { copper: string };
type RecommendationState = 'ready' | 'notSynchronized' | 'accountUnavailable' | 'evidenceUnavailable';
type RecommendationAction =
  | 'BUY' | 'BUY SMALL' | 'WAIT' | 'KEEP BID' | 'UPDATE BID' | 'STOP BIDDING'
  | 'CANCEL BID' | 'LIST' | 'LEAVE SELL LISTING' | 'HOLD' | 'REDUCE'
  | 'SELL PARTIAL' | 'SELL' | 'SKIP' | 'REVIEW';
type RecommendationSource = 'newOpportunity' | 'buyOrder' | 'sellListing' | 'inventory';
type HistoryConfidence = 'insufficient' | 'partial' | 'strong';

type RecommendationRecord = {
  action: RecommendationAction;
  source: RecommendationSource;
  orderState: string;
  orderId: string | null;
  itemId: number;
  itemName: string;
  itemIconUrl: string | null;
  quantity: number;
  capital: Money;
  prices: {
    currentOrderUnitPrice: Money | null;
    bestBuy: Money | null;
    lowestSell: Money | null;
    plannedBid: Money | null;
    plannedListPrice: Money | null;
    maximumBid: Money | null;
  };
  economics: {
    acquisitionCost: Money;
    grossSaleValue: Money;
    listingFee: Money;
    exchangeFee: Money;
    netSaleProceeds: Money;
    netProfit: Money;
    totalCost: Money;
    roiDisplayPercent: string;
  } | null;
  score: {
    rank: number;
    totalPoints: number;
    basePoints: number;
    appliedPenaltyPoints: number;
    components: Array<{ name: string; state: string; normalizedPercent: number; maximumPoints: number; awardedPoints: number }>;
    anomalies: unknown[];
    personalEvidence: {
      state: string;
      knownBasisSampleCount: number | null;
      latestKnownBasisCompletionAtUtc: string | null;
      completionRateLimitation: string;
    } | null;
  } | null;
  history: {
    confidence: HistoryConfidence;
    commonCutoffUtc: string;
    windows: Array<{
      durationDays: number;
      isAvailable: boolean;
      rawObservationCount: number;
      eligibleObservationCount: number;
      observedSpanPercent: number;
    }>;
  } | null;
  liquidity: {
    classification: 'high' | 'medium' | 'low';
    totalBuyQuantity: number;
    totalSellQuantity: number;
    nearBestBuyQuantity: number;
    nearBestSellQuantity: number;
    participationCapQuantity: number;
    safeLiquidationQuantity: number;
    reasons: string[];
  } | null;
  portfolioConstraints: Array<{ name: string; capitalCapacity: Money; quantityCapacity: number; isBinding: boolean }>;
  reasons: Array<{ code: string; message: string }>;
};

type RecommendationResponse = {
  state: RecommendationState;
  evidenceError: string | null;
  generatedAtUtc: string | null;
  lastSuccessfulSyncAtUtc: string | null;
  currentOrdersObservedAtUtc: string | null;
  scannerObservedAtUtc: string | null;
  policies: {
    actionPolicyVersion: number;
    scorePolicyVersion: number;
    positionSizingPolicyVersion: number;
    fifoPolicyVersion: number;
    feePolicyVersion: number;
    minimumProfit: Money;
    minimumRoiBasisPoints: number;
    cashReserveBasisPoints: number;
    strategy: string;
    category: string;
  };
  portfolio: {
    availableCash: Money;
    totalBankroll: Money;
    cashReserve: Money;
    reserveStatus: 'satisfied' | 'breached';
    cashReserveShortfall: Money;
    remainingCashAfterSizing: Money;
  } | null;
  actions: RecommendationRecord[];
};

const allActions: RecommendationAction[] = [
  'BUY', 'BUY SMALL', 'WAIT', 'KEEP BID', 'UPDATE BID', 'STOP BIDDING', 'CANCEL BID',
  'LIST', 'LEAVE SELL LISTING', 'HOLD', 'REDUCE', 'SELL PARTIAL', 'SELL', 'SKIP', 'REVIEW',
];
const sources: RecommendationSource[] = ['newOpportunity', 'buyOrder', 'sellListing', 'inventory'];
const actionable = new Set<RecommendationAction>([
  'BUY', 'BUY SMALL', 'UPDATE BID', 'CANCEL BID',
  'LIST', 'REDUCE', 'SELL PARTIAL', 'SELL',
]);

export default function RecommendationPanel({ refreshGeneration = 0 }: { refreshGeneration?: number }) {
  const [result, setResult] = useState<RecommendationResponse | null>(null);
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const requestGeneration = useRef(0);

  const load = () => {
    const generation = ++requestGeneration.current;
    setStatus('loading');
    void fetch('/api/recommendations', {
      headers: { Accept: 'application/json', 'X-Tyrian-Ledger-Request': '1' },
    }).then(async response => {
      const payload: unknown = await response.json();
      if (generation !== requestGeneration.current) return;
      if (!isRecommendationResponse(payload)) {
        setStatus('error');
        return;
      }
      setResult(payload);
      setStatus('ready');
    }).catch(() => {
      if (generation === requestGeneration.current) setStatus('error');
    });
  };

  useEffect(() => {
    load();
    return () => { requestGeneration.current++; };
  }, [refreshGeneration]);

  const signals = useMemo(
    () => (result?.actions ?? [])
      .filter(record => actionable.has(record.action)),
    [result],
  );
  const historyCutoff = useMemo(() => oldestHistoryCutoff(result?.actions ?? []), [result]);

  return (
    <section aria-labelledby="signals-feed-title" className="signals-feed">
      <div className="signals-toolbar">
        <div>
          <p className="eyebrow">Actions à effectuer dans Guild Wars 2</p>
          <h2 id="signals-feed-title">
            {status === 'ready' && result?.state === 'ready'
              ? signals.length === 0
                ? 'Aucun signal'
                : signals.length === 1
                  ? '1 signal mérite votre attention'
                  : `${signals.length} signaux méritent votre attention`
              : 'Mes Signaux'}
          </h2>
        </div>
        <button className="refresh-signals" disabled={status === 'loading'} onClick={load} type="button">
          {status === 'loading' ? 'Actualisation en cours…' : 'Actualiser'}
        </button>
      </div>

      <OperationalStatus status={status} result={result} />

      {result?.state === 'ready' && (
        <details className="data-freshness">
          <summary>
            <span>{marketFreshness(result.scannerObservedAtUtc)}</span>
            <span className="freshness-auto">Actualisation à la demande</span>
          </summary>
          <div className="freshness-grid">
            <div>
              <strong>Marché</strong>
              <span>{sourceAge(result.scannerObservedAtUtc, 'Aucune observation récente', 'Actualisé')}</span>
            </div>
            <div>
              <strong>Compte ArenaNet</strong>
              <span>{sourceAge(result.lastSuccessfulSyncAtUtc, "Pas encore synchronisé", 'Synchronisé')}</span>
            </div>
            <div>
              <strong>Historique marché</strong>
              <span>{sourceAge(historyCutoff, 'Aucun échantillon exploitable', 'Dernier échantillon enregistré')}</span>
            </div>
          </div>
        </details>
      )}

      {status === 'ready' && result?.state === 'ready' && signals.length === 0 && (
        <div className="signals-zero-state">
          <span aria-hidden="true" className="zero-state-mark">✓</span>
          <div>
            <h3>Aucun signal ne mérite votre attention pour le moment.</h3>
            <p>Les données actuelles ne justifient aucune action.</p>
          </div>
        </div>
      )}

      {status === 'ready' && result?.state === 'ready' && signals.length > 0 && (
        <ol className="signal-list">
          {signals.map((record, index) => (
            <SignalCard
              key={`${record.source}-${record.orderId ?? record.itemId}-${index}`}
              record={record}
            />
          ))}
        </ol>
      )}
    </section>
  );
}

function OperationalStatus({
  status,
  result,
}: {
  status: 'loading' | 'ready' | 'error';
  result: RecommendationResponse | null;
}) {
  if (status === 'loading') {
    return <p aria-live="polite" className="operational-status" role="status">Chargement des données…</p>;
  }
  if (status === 'error') {
    return <p className="operational-status operational-status--error" role="alert">La lecture des signaux depuis l'application locale a échoué.</p>;
  }
  if (result?.state === 'notSynchronized') {
    return <p className="operational-status operational-status--warning" role="status">Aucun compte synchronisé. Synchronisez vos données ArenaNet pour calculer les signaux.</p>;
  }
  if (result?.state === 'accountUnavailable') {
    return <p className="operational-status operational-status--warning" role="status">Le compte ArenaNet n'est pas disponible. Les signaux dépendant du compte sont masqués.</p>;
  }
  if (result?.state === 'evidenceUnavailable') {
    return (
      <p className="operational-status operational-status--warning" role="status">
        ArenaNet ou les données de marché sont temporairement indisponibles. Aucun signal incomplet n'est affiché.
      </p>
    );
  }
  if (result?.evidenceError) {
    return <p className="operational-status operational-status--warning" role="status">Certaines données sont limitées. Les signaux affichés restent fondés sur les preuves disponibles.</p>;
  }
  return null;
}

function SignalCard({ record }: { record: RecommendationRecord }) {
  const [whyOpen, setWhyOpen] = useState(false);
  const binding = record.portfolioConstraints.filter(constraint => constraint.isBinding);
  const price = executionPrice(record);
  const maxPrice = ['BUY', 'BUY SMALL', 'UPDATE BID'].includes(record.action)
    ? record.prices.maximumBid
    : null;
  const corrective = ['CANCEL BID', 'REDUCE'].includes(record.action);
  const supporting = supportingMetric(record);

  return (
    <li className={`signal-card signal-card--${tone(record.action)}`}>
      <article aria-labelledby={`signal-${record.source}-${record.orderId ?? record.itemId}`}>
        <div className="signal-card-header">
          <ItemVisual url={record.itemIconUrl} />
          <div className="signal-identity">
            <div className="signal-badges">
              {corrective && <span className="priority-badge">Prioritaire</span>}
              <span className="action-badge">{actionLabel(record.action)}</span>
            </div>
            <h3 id={`signal-${record.source}-${record.orderId ?? record.itemId}`}>{record.itemName}</h3>
          </div>
          <span className="confidence-badge">Confiance {confidenceLabel(record.history?.confidence)}</span>
        </div>

        <div className="execution-block">
          <div className="execution-field">
            <span className="execution-label">{quantityLabel(record.action)}</span>
            <strong className="execution-quantity">{record.quantity}</strong>
          </div>
          <div className="execution-field execution-field--price">
            <span className="execution-label">{priceLabel(record.action)}</span>
            {price
              ? <MoneyDisplay className="execution-price" money={price} />
              : <strong className="execution-unavailable">À vérifier</strong>}
          </div>
          {maxPrice && !sameMoney(price, maxPrice) && (
            <div className="execution-limit">
              <span>Ne pas dépasser</span>
              <MoneyDisplay compact money={maxPrice} />
            </div>
          )}
        </div>

        <div className="signal-supporting">
          <div>
            <span>{supporting.label}</span>
            {supporting.money
              ? <MoneyDisplay className="modeled-profit" compact money={supporting.money} />
              : <strong>Indisponible</strong>}
          </div>
          <details className="why-disclosure" onToggle={event => setWhyOpen(event.currentTarget.open)}>
            <summary aria-label={whyOpen ? "Masquer l'explication de ce signal" : 'Afficher pourquoi ce signal est recommandé'}>Pourquoi ?</summary>
            <div className="why-content">
              <section>
                <h4>Pourquoi maintenant ?</h4>
                <ul>
                  {record.reasons
                    .filter(reason => reason.code !== 'readOnlyManualAction')
                    .map(reason => <li key={reason.code}>{reasonLabel(reason.code)}</li>)}
                </ul>
              </section>
              <section>
                <h4>Marché</h4>
                <p>
                  {record.liquidity
                    ? `Liquidité ${liquidityLabel(record.liquidity.classification)} · ${record.liquidity.nearBestBuyQuantity} à l'achat / ${record.liquidity.nearBestSellQuantity} à la vente près du meilleur prix.`
                    : 'La profondeur de marché détaillée n’est pas disponible.'}
                </p>
                {record.history && <p>Historique retenu : confiance {confidenceLabel(record.history.confidence)}.</p>}
              </section>
              <section>
                <h4>Votre situation</h4>
                <p>
                  {binding.length === 0
                    ? 'La quantité proposée respecte les contraintes de portefeuille actuellement connues.'
                    : `Contrainte active : ${binding.map(value => constraintLabel(value.name)).join(', ')}.`}
                </p>
                {record.score?.personalEvidence && <p>{personalEvidenceSummary(record.score.personalEvidence)}</p>}
              </section>
              <section>
                <h4>Risques et limites</h4>
                <p>Le profit est modélisé. Le remplissage, le délai et le prix final ne sont pas garantis.</p>
                {record.economics && <p>ROI modélisé : {record.economics.roiDisplayPercent}.</p>}
              </section>
              <section>
                <h4>Fraîcheur des données</h4>
                <p>{record.history ? `Historique marché : ${sourceAge(record.history.commonCutoffUtc, 'inconnu', 'Dernier échantillon enregistré')}.` : 'Historique marché : inconnu.'}</p>
              </section>
            </div>
          </details>
        </div>
      </article>
    </li>
  );
}

function ItemVisual({ url }: { url: string | null }) {
  const [failed, setFailed] = useState(false);
  return (
    <span aria-hidden="true" className="item-visual">
      {url && !failed
        ? <img alt="" onError={() => setFailed(true)} src={url} />
        : <span className="item-fallback">◇</span>}
    </span>
  );
}

function executionPrice(record: RecommendationRecord): Money | null {
  switch (record.action) {
    case 'BUY':
    case 'BUY SMALL':
    case 'UPDATE BID':
      return record.prices.plannedBid;
    case 'CANCEL BID':
    case 'STOP BIDDING':
      return record.prices.currentOrderUnitPrice;
    case 'LIST':
      return record.prices.plannedListPrice;
    case 'REDUCE':
    case 'SELL PARTIAL':
    case 'SELL':
      return record.prices.bestBuy;
    default:
      return null;
  }
}

function actionLabel(action: RecommendationAction): string {
  switch (action) {
    case 'BUY':
    case 'BUY SMALL': return "PLACER UN ORDRE D'ACHAT";
    case 'UPDATE BID': return "METTRE À JOUR L'ORDRE D'ACHAT";
    case 'CANCEL BID': return "ANNULER L'ORDRE D'ACHAT";
    case 'LIST': return 'METTRE EN VENTE';
    case 'REDUCE':
    case 'SELL PARTIAL': return 'VENDRE PARTIELLEMENT';
    case 'SELL': return 'VENDRE MAINTENANT';
    default: return action;
  }
}

function quantityLabel(action: RecommendationAction): string {
  if (action === 'CANCEL BID') return 'Quantité concernée';
  if (['LIST', 'REDUCE', 'SELL PARTIAL', 'SELL'].includes(action)) return 'Quantité à vendre';
  return 'Quantité à saisir';
}

function priceLabel(action: RecommendationAction): string {
  if (action === 'CANCEL BID') return "Prix actuel de l'ordre";
  if (action === 'LIST') return 'Prix de vente par unité';
  if (['REDUCE', 'SELL PARTIAL', 'SELL'].includes(action)) return 'Prix de vente immédiate par unité';
  return 'Prix à saisir par unité';
}

function supportingMetric(record: RecommendationRecord): { label: string; money: Money | null } {
  if (record.action === 'CANCEL BID') {
    return { label: 'Capital à libérer', money: record.capital };
  }

  return {
    label: 'Profit modélisé',
    money: record.economics?.netProfit ?? null,
  };
}

function personalEvidenceSummary(evidence: NonNullable<NonNullable<RecommendationRecord['score']>['personalEvidence']>): string {
  const samples = evidence.knownBasisSampleCount ?? 0;
  switch (evidence.state) {
    case 'supported':
      return `Historique personnel : ${samples} résultat${samples === 1 ? '' : 's'} à base connue pris en compte.`;
    case 'stale':
      return 'Historique personnel disponible, mais trop ancien pour constituer une preuve forte.';
    case 'insufficientSamples':
      return `Historique personnel : seulement ${samples} résultat${samples === 1 ? '' : 's'} à base connue, encore insuffisant.`;
    case 'insufficientCoverage':
      return 'Historique personnel : couverture continue insuffisante.';
    case 'insufficientMetrics':
      return 'Historique personnel disponible, mais métriques insuffisantes.';
    case 'noHistory':
    case 'notYetAvailable':
      return 'Historique personnel : pas encore de preuve exploitable.';
    default:
      return 'Historique personnel : preuve limitée.';
  }
}

function confidenceLabel(confidence: HistoryConfidence | undefined): string {
  switch (confidence) {
    case 'strong': return 'élevée';
    case 'partial': return 'moyenne';
    default: return 'limitée';
  }
}

function liquidityLabel(value: RecommendationRecord['liquidity'] extends infer T ? T extends { classification: infer C } ? C : never : never): string {
  switch (value) {
    case 'high': return 'élevée';
    case 'medium': return 'moyenne';
    case 'low': return 'limitée';
  }
}

function reasonLabel(code: string): string {
  const labels: Record<string, string> = {
    strongEvidence: 'Les fenêtres historiques disponibles soutiennent cette opportunité.',
    partialHistory: 'L’historique est partiel : la taille proposée reste volontairement réduite.',
    highLiquidity: 'La profondeur visible ne présente pas d’alerte de liquidité.',
    liquidityRisk: 'La profondeur ou un écart de prix justifie une quantité plus prudente.',
    bidAboveMaximum: 'L’ordre actuel dépasse le prix maximal autorisé par la politique.',
    reserveRestoration: 'Cette annulation libère du capital pour restaurer la réserve.',
    bidOutbid: 'L’ordre est dépassé par la meilleure offre visible.',
    updateWithinMaximum: 'Le nouveau prix reste sous le maximum autorisé.',
    incrementalCapitalAvailable: 'Le capital supplémentaire requis est disponible.',
    incrementalCapitalUnavailable: 'Le capital supplémentaire requis n’est pas disponible.',
    itemExposureExceeded: 'L’exposition sur cet objet dépasse la limite actuelle.',
    safeDepthLimited: 'La quantité est limitée par la profondeur de marché actuellement sûre.',
    positiveImmediateExit: 'La vente immédiate modélisée reste positive après les frais.',
    positiveListingExit: 'La mise en vente modélisée reste positive après les frais.',
    strategyExposureExceeded: 'L’exposition de la stratégie dépasse la limite actuelle.',
    categoryExposureExceeded: 'L’exposition de la catégorie dépasse la limite actuelle.',
    penalizedEvidence: 'Les preuves comportent une anomalie non critique prise en compte dans le classement.',
    fullBuyEvidenceNotMet: 'Les preuves ne justifient pas une augmentation agressive de l’ordre.',
  };
  return labels[code] ?? 'Les preuves structurées du moteur justifient cette action.';
}

function constraintLabel(value: string): string {
  const labels: Record<string, string> = {
    itemExposure: 'exposition sur l’objet',
    strategyConcentration: 'concentration de stratégie',
    categoryConcentration: 'concentration de catégorie',
    liquidityParticipation: 'liquidité disponible',
    availableCash: 'capital disponible',
    cashReserve: 'réserve de sécurité',
    cashAfterReserve: 'capital disponible après réserve',
  };
  return labels[value] ?? value;
}

function tone(action: RecommendationAction): string {
  if (['BUY', 'BUY SMALL', 'UPDATE BID'].includes(action)) return 'buy';
  if (['LIST', 'SELL', 'SELL PARTIAL'].includes(action)) return 'sell';
  if (['CANCEL BID', 'REDUCE'].includes(action)) return 'corrective';
  return 'neutral';
}

function marketFreshness(timestamp: string | null): string {
  return timestamp ? `Marché actualisé ${agePhrase(timestamp)}` : 'Marché : aucune observation récente';
}

function sourceAge(timestamp: string | null, fallback: string, prefix: string): string {
  return timestamp ? `${prefix} ${agePhrase(timestamp)}` : fallback;
}

function agePhrase(timestamp: string): string {
  const elapsedSeconds = Math.max(0, Math.floor((Date.now() - new Date(timestamp).getTime()) / 1000));
  if (elapsedSeconds < 60) return `il y a ${elapsedSeconds} s`;
  const minutes = Math.floor(elapsedSeconds / 60);
  if (minutes < 60) return `il y a ${minutes} min`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `il y a ${hours} h`;
  return `le ${new Date(timestamp).toLocaleString('fr-FR')}`;
}

function oldestHistoryCutoff(records: RecommendationRecord[]): string | null {
  const timestamps = records
    .map(record => record.history?.commonCutoffUtc ?? null)
    .filter((value): value is string => value !== null)
    .map(value => ({ value, time: new Date(value).getTime() }))
    .filter(value => Number.isFinite(value.time))
    .sort((a, b) => a.time - b.time);
  return timestamps[0]?.value ?? null;
}

function sameMoney(left: Money | null, right: Money | null): boolean {
  return left !== null && right !== null && left.copper === right.copper;
}

function isRecommendationResponse(value: unknown): value is RecommendationResponse {
  if (!isRecord(value) || !['ready', 'notSynchronized', 'accountUnavailable', 'evidenceUnavailable'].includes(value.state as string)) return false;
  if (!isPolicies(value.policies) || !Array.isArray(value.actions)) return false;
  if (!isNullableString(value.evidenceError) || !isNullableString(value.generatedAtUtc)
    || !isNullableString(value.lastSuccessfulSyncAtUtc) || !isNullableString(value.currentOrdersObservedAtUtc)
    || !isNullableString(value.scannerObservedAtUtc)) return false;
  return value.actions.every(isRecommendationRecord)
    && (value.portfolio === null || (isRecord(value.portfolio)
      && isMoney(value.portfolio.availableCash) && isMoney(value.portfolio.totalBankroll)
      && isMoney(value.portfolio.cashReserve) && ['satisfied', 'breached'].includes(value.portfolio.reserveStatus as string)
      && isMoney(value.portfolio.cashReserveShortfall) && isMoney(value.portfolio.remainingCashAfterSizing)));
}

function isRecommendationRecord(value: unknown): value is RecommendationRecord {
  if (!isRecord(value) || !allActions.includes(value.action as RecommendationAction) || !sources.includes(value.source as RecommendationSource)) return false;
  return typeof value.itemId === 'number' && Number.isSafeInteger(value.itemId) && value.itemId > 0
    && typeof value.itemName === 'string'
    && (value.itemIconUrl === null || value.itemIconUrl === undefined || typeof value.itemIconUrl === 'string')
    && isNonNegativeInteger(value.quantity) && isMoney(value.capital)
    && isRecord(value.prices) && isNullableMoney(value.prices.currentOrderUnitPrice)
    && isNullableMoney(value.prices.bestBuy) && isNullableMoney(value.prices.lowestSell)
    && isNullableMoney(value.prices.plannedBid) && isNullableMoney(value.prices.plannedListPrice)
    && isNullableMoney(value.prices.maximumBid)
    && Array.isArray(value.portfolioConstraints) && value.portfolioConstraints.every(isPortfolioConstraint)
    && Array.isArray(value.reasons) && value.reasons.every(reason => isRecord(reason) && typeof reason.code === 'string' && typeof reason.message === 'string')
    && (value.economics === null || isEconomics(value.economics))
    && (value.score === null || isScore(value.score))
    && (value.history === null || isHistory(value.history))
    && (value.liquidity === null || isLiquidity(value.liquidity));
}

function isPolicies(value: unknown): boolean {
  return isRecord(value)
    && ['actionPolicyVersion', 'scorePolicyVersion', 'positionSizingPolicyVersion', 'fifoPolicyVersion', 'feePolicyVersion', 'minimumRoiBasisPoints', 'cashReserveBasisPoints'].every(key => isNonNegativeInteger(value[key]))
    && isMoney(value.minimumProfit)
    && typeof value.strategy === 'string' && typeof value.category === 'string';
}
function isEconomics(value: unknown): boolean {
  return isRecord(value)
    && ['acquisitionCost', 'grossSaleValue', 'listingFee', 'exchangeFee', 'netSaleProceeds', 'netProfit', 'totalCost'].every(key => isMoney(value[key]))
    && typeof value.roiDisplayPercent === 'string';
}
function isScore(value: unknown): boolean {
  return isRecord(value)
    && isNonNegativeInteger(value.rank)
    && ['totalPoints', 'basePoints', 'appliedPenaltyPoints'].every(key => typeof value[key] === 'number' && Number.isFinite(value[key]))
    && Array.isArray(value.components)
    && Array.isArray(value.anomalies)
    && (value.personalEvidence === undefined || value.personalEvidence === null || isRecord(value.personalEvidence));
}
function isHistory(value: unknown): boolean {
  return isRecord(value)
    && ['insufficient', 'partial', 'strong'].includes(value.confidence as string)
    && typeof value.commonCutoffUtc === 'string'
    && Array.isArray(value.windows)
    && value.windows.every(window => isRecord(window)
      && isNonNegativeInteger(window.durationDays)
      && typeof window.isAvailable === 'boolean'
      && isNonNegativeInteger(window.rawObservationCount)
      && isNonNegativeInteger(window.eligibleObservationCount)
      && typeof window.observedSpanPercent === 'number'
      && Number.isFinite(window.observedSpanPercent));
}
function isLiquidity(value: unknown): boolean {
  return isRecord(value)
    && ['high', 'medium', 'low'].includes(value.classification as string)
    && ['totalBuyQuantity', 'totalSellQuantity', 'nearBestBuyQuantity', 'nearBestSellQuantity', 'participationCapQuantity', 'safeLiquidationQuantity'].every(key => isNonNegativeInteger(value[key]))
    && Array.isArray(value.reasons);
}
function isPortfolioConstraint(value: unknown): boolean {
  return isRecord(value)
    && typeof value.name === 'string'
    && isMoney(value.capitalCapacity)
    && isNonNegativeInteger(value.quantityCapacity)
    && typeof value.isBinding === 'boolean';
}
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null; }
function isMoney(value: unknown): value is Money { return isRecord(value) && typeof value.copper === 'string' && /^-?\d+$/.test(value.copper); }
function isNullableMoney(value: unknown): value is Money | null { return value === null || isMoney(value); }
function isNullableString(value: unknown): value is string | null { return value === null || typeof value === 'string'; }
function isNonNegativeInteger(value: unknown): boolean { return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0; }
