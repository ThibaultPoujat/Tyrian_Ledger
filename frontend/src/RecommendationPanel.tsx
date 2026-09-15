import { useEffect, useRef, useState } from 'react';

type Money = { copper: string };
type RecommendationState = 'ready' | 'notSynchronized' | 'accountUnavailable' | 'evidenceUnavailable';
type RecommendationAction =
  | 'BUY' | 'BUY SMALL' | 'WAIT' | 'KEEP BID' | 'UPDATE BID' | 'STOP BIDDING'
  | 'CANCEL BID' | 'LIST' | 'LEAVE SELL LISTING' | 'HOLD' | 'REDUCE'
  | 'SELL PARTIAL' | 'SELL' | 'SKIP' | 'REVIEW';
type RecommendationSource = 'newOpportunity' | 'buyOrder' | 'sellListing' | 'inventory';

type RecommendationRecord = {
  action: RecommendationAction; source: RecommendationSource; orderState: string;
  orderId: string | null; itemId: number; itemName: string; quantity: number; capital: Money;
  prices: { currentOrderUnitPrice: Money | null; bestBuy: Money | null; lowestSell: Money | null; plannedBid: Money | null; plannedListPrice: Money | null; maximumBid: Money | null };
  economics: { acquisitionCost: Money; grossSaleValue: Money; listingFee: Money; exchangeFee: Money; netSaleProceeds: Money; netProfit: Money; totalCost: Money; roiDisplayPercent: string } | null;
  score: {
    rank: number; totalPoints: number; basePoints: number; appliedPenaltyPoints: number;
    components: Array<{ name: string; state: string; normalizedPercent: number; maximumPoints: number; awardedPoints: number }>;
    anomalies: unknown[];
    personalEvidence: {
      state: 'noHistory' | 'insufficientCoverage' | 'insufficientSamples' | 'insufficientMetrics' | 'stale' | 'supported';
      knownBasisSampleCount: number | null; latestKnownBasisCompletionAtUtc: string | null;
      realizedRoiSaleCount: number | null; minimumRealizedRoiBasisPoints: number | null;
      medianRealizedRoiBasisPoints: number | null; maximumRealizedRoiBasisPoints: number | null;
      realizedProfitPerDayNumerator: string | null; realizedProfitPerDayDenominator: string | null;
      capitalTurnsPerDayNumerator: string | null; capitalTurnsPerDayDenominator: string | null;
      typicalHoldingDuration: string | null; completionRateLimitation: string;
    } | null;
  } | null;
  history: { confidence: 'insufficient' | 'partial' | 'strong'; commonCutoffUtc: string; windows: Array<{ durationDays: number; isAvailable: boolean; rawObservationCount: number; eligibleObservationCount: number; observedSpanPercent: number }> } | null;
  liquidity: { classification: 'high' | 'medium' | 'low'; totalBuyQuantity: number; totalSellQuantity: number; nearBestBuyQuantity: number; nearBestSellQuantity: number; participationCapQuantity: number; safeLiquidationQuantity: number; reasons: string[] } | null;
  portfolioConstraints: Array<{ name: string; capitalCapacity: Money; quantityCapacity: number; isBinding: boolean }>;
  reasons: Array<{ code: string; message: string }>;
};

type RecommendationResponse = {
  state: RecommendationState; evidenceError: string | null; generatedAtUtc: string | null;
  lastSuccessfulSyncAtUtc: string | null; currentOrdersObservedAtUtc: string | null; scannerObservedAtUtc: string | null;
  policies: { actionPolicyVersion: number; scorePolicyVersion: number; positionSizingPolicyVersion: number; fifoPolicyVersion: number; feePolicyVersion: number; minimumProfit: Money; minimumRoiBasisPoints: number; cashReserveBasisPoints: number; strategy: string; category: string };
  portfolio: { availableCash: Money; totalBankroll: Money; cashReserve: Money; reserveStatus: 'satisfied' | 'breached'; cashReserveShortfall: Money; remainingCashAfterSizing: Money } | null;
  actions: RecommendationRecord[];
};

const actions: RecommendationAction[] = [
  'BUY', 'BUY SMALL', 'WAIT', 'KEEP BID', 'UPDATE BID', 'STOP BIDDING', 'CANCEL BID',
  'LIST', 'LEAVE SELL LISTING', 'HOLD', 'REDUCE', 'SELL PARTIAL', 'SELL', 'SKIP', 'REVIEW',
];
const sources: RecommendationSource[] = ['newOpportunity', 'buyOrder', 'sellListing', 'inventory'];

export default function RecommendationPanel({ refreshGeneration = 0 }: { refreshGeneration?: number }) {
  const [result, setResult] = useState<RecommendationResponse | null>(null);
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [showAllOpportunities, setShowAllOpportunities] = useState(false);
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
      setShowAllOpportunities(false);
      setStatus('ready');
    }).catch(() => {
      if (generation === requestGeneration.current) setStatus('error');
    });
  };

  useEffect(() => {
    load();
    return () => { requestGeneration.current++; };
  }, [refreshGeneration]);

  const newOpportunityCount = result?.actions.filter(action => action.source === 'newOpportunity').length ?? 0;
  let visibleNewOpportunities = 0;
  const visibleActions = result?.actions.filter(action => {
    if (action.source !== 'newOpportunity' || showAllOpportunities) return true;
    visibleNewOpportunities++;
    return visibleNewOpportunities <= 5;
  }) ?? [];

  return (
    <section aria-labelledby="recommendation-list-title" className="recommendation-panel">
      <div className="recommendation-toolbar">
        <div><p className="eyebrow">Attention first</p><h2 id="recommendation-list-title">Your next manual actions</h2></div>
        <button disabled={status === 'loading'} onClick={load} type="button">
          {status === 'loading' ? 'Refreshing…' : 'Refresh actions'}
        </button>
      </div>
      <p aria-live="polite" className="recommendation-status" role="status">
        {status === 'loading' && 'Checking cash, orders, market depth, and retained history…'}
        {status === 'error' && 'Recommendations could not be read from the local host.'}
        {status === 'ready' && result?.state === 'ready' && `${result.actions.length} manual action${result.actions.length === 1 ? '' : 's'} ready to review.`}
        {status === 'ready' && result?.state === 'notSynchronized' && 'Synchronize Trading Post data to build your action list.'}
        {status === 'ready' && result?.state === 'accountUnavailable' && 'A ready ArenaNet account key and wallet balance are required.'}
        {status === 'ready' && result?.state === 'evidenceUnavailable' && 'Current market or history evidence is temporarily unavailable. No action is recommended.'}
      </p>
      {result?.state === 'ready' && result.portfolio && (
        <div className="recommendation-portfolio" aria-label="Portfolio sizing status">
          <span>Available cash <strong>{copper(result.portfolio.availableCash)}</strong></span>
          <span>Reserve <strong>{copper(result.portfolio.cashReserve)}</strong></span>
          <span>Reserve status <strong>{humanize(result.portfolio.reserveStatus)}</strong></span>
          <span>After sizing <strong>{copper(result.portfolio.remainingCashAfterSizing)}</strong></span>
        </div>
      )}
      {result?.state === 'ready' && visibleActions.length === 0 && <p>No supported action has complete evidence yet.</p>}
      {result?.state === 'ready' && visibleActions.length > 0 && (
        <ol className="recommendation-list">
          {visibleActions.map((record, index) => <RecommendationCard key={`${record.source}-${record.orderId ?? record.itemId}-${index}`} record={record} />)}
        </ol>
      )}
      {result?.state === 'ready' && newOpportunityCount > 5 && (
        <button className="show-opportunities" onClick={() => setShowAllOpportunities(value => !value)} type="button">
          {showAllOpportunities ? 'Show top 5 new opportunities' : `Show ${newOpportunityCount - 5} more new opportunities`}
        </button>
      )}
      {result?.generatedAtUtc && <p className="recommendation-timestamp">Generated {new Date(result.generatedAtUtc).toLocaleString()} · Policy v{result.policies.actionPolicyVersion}</p>}
    </section>
  );
}

function RecommendationCard({ record }: { record: RecommendationRecord }) {
  const primaryPrice = record.source === 'sellListing'
    ? record.prices.currentOrderUnitPrice
    : record.source === 'buyOrder'
      ? record.action === 'UPDATE BID' ? record.prices.plannedBid : record.prices.currentOrderUnitPrice
      : record.action === 'LIST'
        ? record.prices.plannedListPrice
        : ['SELL', 'SELL PARTIAL', 'REDUCE'].includes(record.action)
          ? record.prices.bestBuy
          : record.prices.plannedBid ?? record.prices.currentOrderUnitPrice;
  const binding = record.portfolioConstraints.filter(constraint => constraint.isBinding);
  return (
    <li className={`recommendation-card recommendation-card--${tone(record.action)}`}>
      <article aria-labelledby={`recommendation-${record.source}-${record.orderId ?? record.itemId}`}>
        <header>
          <span className="action-badge">{record.action}</span>
          <span className="recommendation-source">{sourceName(record.source)}</span>
          <h3 id={`recommendation-${record.source}-${record.orderId ?? record.itemId}`}>{record.itemName}</h3>
        </header>
        <dl className="recommendation-summary">
          <div><dt>Quantity</dt><dd>{record.quantity}</dd></div>
          <div><dt>{record.source === 'newOpportunity' ? 'Capital' : 'Basis / capital'}</dt><dd>{copper(record.capital)}</dd></div>
          <div><dt>Price</dt><dd>{primaryPrice ? copper(primaryPrice) : 'Review evidence'}</dd></div>
          <div><dt>Max bid</dt><dd>{record.prices.maximumBid ? copper(record.prices.maximumBid) : 'Not applicable'}</dd></div>
          <div><dt>Modeled profit</dt><dd>{record.economics ? copper(record.economics.netProfit) : 'Unavailable'}</dd></div>
          <div><dt>ROI</dt><dd>{record.economics?.roiDisplayPercent ?? 'Unavailable'}</dd></div>
        </dl>
        <p className="recommendation-reason"><strong>Why:</strong> {record.reasons[0]?.message ?? 'Review the supporting evidence.'}</p>
        <p className="recommendation-confidence">
          Confidence <strong>{record.history ? humanize(record.history.confidence) : 'Unavailable'}</strong>
          {' · '}Liquidity <strong>{record.liquidity ? humanize(record.liquidity.classification) : 'Unavailable'}</strong>
          {' · '}Constraint <strong>{binding.length > 0 ? binding.map(value => humanize(value.name)).join(', ') : 'None binding'}</strong>
        </p>
        <details>
          <summary>Review depth, history, score, and reasons</summary>
          <div className="recommendation-evidence">
            <section><h4>Current evidence</h4><p>Best buy {record.prices.bestBuy ? copper(record.prices.bestBuy) : 'unavailable'} · Lowest sell {record.prices.lowestSell ? copper(record.prices.lowestSell) : 'unavailable'} · Order {humanize(record.orderState)}</p><p>{record.liquidity ? `${record.liquidity.totalBuyQuantity} buy / ${record.liquidity.totalSellQuantity} sell quantity visible; participation cap ${record.liquidity.participationCapQuantity}.` : 'Depth unavailable.'}</p></section>
            <section><h4>Retained history</h4>{record.history ? record.history.windows.map(window => <p key={window.durationDays}>{window.durationDays} days: {window.eligibleObservationCount}/{window.rawObservationCount} eligible, {window.observedSpanPercent}% span, {window.isAvailable ? 'available' : 'insufficient'}.</p>) : <p>History unavailable.</p>}</section>
            <section><h4>Score and constraints</h4><p>{record.score ? `Rank ${record.score.rank}, ${record.score.totalPoints.toFixed(2)} points after ${record.score.appliedPenaltyPoints.toFixed(2)} penalty.` : 'Score unavailable.'}</p>{record.score && <p>{record.score.components.map(component => `${humanize(component.name)}: ${component.awardedPoints.toFixed(2)} / ${component.name === 'personalEvidence' ? '±' : ''}${component.maximumPoints.toFixed(2)} (${humanize(component.state)})`).join(' · ')}</p>}{record.score?.personalEvidence && <PersonalEvidence evidence={record.score.personalEvidence} />}<p>{binding.length > 0 ? binding.map(value => `${humanize(value.name)}: ${value.quantityCapacity} units`).join(' · ') : 'No binding position-size constraint.'}</p></section>
            <section><h4>All reasons</h4><ul>{record.reasons.map(reason => <li key={reason.code}>{reason.message}</li>)}</ul></section>
          </div>
        </details>
      </article>
    </li>
  );
}

function PersonalEvidence({ evidence }: { evidence: NonNullable<NonNullable<RecommendationRecord['score']>['personalEvidence']> }) {
  const basisPoints = (value: number | null) => value === null ? 'unavailable' : `${(value / 100).toFixed(2)}%`;
  const fraction = (numerator: string | null, denominator: string | null, unit: string) =>
    numerator === null || denominator === null ? 'unavailable' : `${numerator} / ${denominator} ${unit}`;
  return <div className="personal-evidence">
    <p>Personal evidence <strong>{humanize(evidence.state)}</strong>
      {' · '}{evidence.knownBasisSampleCount ?? 0} known-basis sales
      {' · '}latest known-basis completion {evidence.latestKnownBasisCompletionAtUtc ?? 'unavailable'}.</p>
    <p>Realized ROI: minimum {basisPoints(evidence.minimumRealizedRoiBasisPoints)} · median {basisPoints(evidence.medianRealizedRoiBasisPoints)} · maximum {basisPoints(evidence.maximumRealizedRoiBasisPoints)}.</p>
    <p>Realized profit/day: {fraction(evidence.realizedProfitPerDayNumerator, evidence.realizedProfitPerDayDenominator, 'copper/day')} · capital turns/day: {fraction(evidence.capitalTurnsPerDayNumerator, evidence.capitalTurnsPerDayDenominator, 'turns/day')} · typical hold {evidence.typicalHoldingDuration ?? 'unavailable'}.</p>
    <p>{evidence.completionRateLimitation}</p>
  </div>;
}

function isRecommendationResponse(value: unknown): value is RecommendationResponse {
  if (!isRecord(value) || !['ready', 'notSynchronized', 'accountUnavailable', 'evidenceUnavailable'].includes(value.state as string)) return false;
  if (!isPolicies(value.policies) || !Array.isArray(value.actions)) return false;
  return value.actions.every(isRecommendationRecord)
    && (value.portfolio === null || (isRecord(value.portfolio)
      && isMoney(value.portfolio.availableCash) && isMoney(value.portfolio.totalBankroll)
      && isMoney(value.portfolio.cashReserve) && ['satisfied', 'breached'].includes(value.portfolio.reserveStatus as string)
      && isMoney(value.portfolio.cashReserveShortfall) && isMoney(value.portfolio.remainingCashAfterSizing)));
}

function isRecommendationRecord(value: unknown): value is RecommendationRecord {
  if (!isRecord(value) || !actions.includes(value.action as RecommendationAction) || !sources.includes(value.source as RecommendationSource)) return false;
  return typeof value.itemId === 'number' && Number.isSafeInteger(value.itemId) && value.itemId > 0
    && typeof value.itemName === 'string' && isNonNegativeInteger(value.quantity) && isMoney(value.capital)
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
  return isRecord(value) && isNonNegativeInteger(value.rank)
    && ['totalPoints', 'basePoints', 'appliedPenaltyPoints'].every(key => typeof value[key] === 'number' && Number.isFinite(value[key]))
    && Array.isArray(value.components) && value.components.every(component => isRecord(component)
      && typeof component.name === 'string' && typeof component.state === 'string'
      && ['normalizedPercent', 'maximumPoints', 'awardedPoints'].every(key => typeof component[key] === 'number' && Number.isFinite(component[key])))
    && Array.isArray(value.anomalies)
    && (value.personalEvidence === undefined || value.personalEvidence === null || isPersonalEvidence(value.personalEvidence));
}
function isPersonalEvidence(value: unknown): boolean {
  return isRecord(value) && ['noHistory', 'insufficientCoverage', 'insufficientSamples', 'insufficientMetrics', 'stale', 'supported'].includes(value.state as string)
    && isNullableNonNegativeInteger(value.knownBasisSampleCount) && (value.latestKnownBasisCompletionAtUtc === null || typeof value.latestKnownBasisCompletionAtUtc === 'string')
    && isNullableNonNegativeInteger(value.realizedRoiSaleCount)
    && ['minimumRealizedRoiBasisPoints', 'medianRealizedRoiBasisPoints', 'maximumRealizedRoiBasisPoints'].every(key => value[key] === null || (typeof value[key] === 'number' && Number.isFinite(value[key])))
    && ['realizedProfitPerDayNumerator', 'realizedProfitPerDayDenominator', 'capitalTurnsPerDayNumerator', 'capitalTurnsPerDayDenominator', 'typicalHoldingDuration'].every(key => value[key] === null || typeof value[key] === 'string')
    && typeof value.completionRateLimitation === 'string';
}
function isHistory(value: unknown): boolean {
  return isRecord(value) && ['insufficient', 'partial', 'strong'].includes(value.confidence as string)
    && typeof value.commonCutoffUtc === 'string' && Array.isArray(value.windows)
    && value.windows.every(window => isRecord(window) && isNonNegativeInteger(window.durationDays)
      && typeof window.isAvailable === 'boolean' && isNonNegativeInteger(window.rawObservationCount)
      && isNonNegativeInteger(window.eligibleObservationCount) && typeof window.observedSpanPercent === 'number'
      && Number.isFinite(window.observedSpanPercent));
}
function isLiquidity(value: unknown): boolean {
  return isRecord(value) && ['high', 'medium', 'low'].includes(value.classification as string)
    && ['totalBuyQuantity', 'totalSellQuantity', 'nearBestBuyQuantity', 'nearBestSellQuantity', 'participationCapQuantity', 'safeLiquidationQuantity'].every(key => isNonNegativeInteger(value[key]))
    && Array.isArray(value.reasons) && value.reasons.every(reason => typeof reason === 'string');
}
function isPortfolioConstraint(value: unknown): boolean {
  return isRecord(value) && typeof value.name === 'string' && isMoney(value.capitalCapacity)
    && isNonNegativeInteger(value.quantityCapacity) && typeof value.isBinding === 'boolean';
}
function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === 'object' && value !== null; }
function isMoney(value: unknown): value is Money { return isRecord(value) && typeof value.copper === 'string' && /^-?\d+$/.test(value.copper); }
function isNullableMoney(value: unknown): value is Money | null { return value === null || isMoney(value); }
function isNonNegativeInteger(value: unknown): boolean { return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0; }
function isNullableNonNegativeInteger(value: unknown): boolean { return value === null || isNonNegativeInteger(value); }
function copper(money: Money): string {
  const value = BigInt(money.copper); const sign = value < 0n ? '−' : ''; const absolute = value < 0n ? -value : value;
  return `${sign}${absolute / 10000n}g ${(absolute % 10000n) / 100n}s ${absolute % 100n}c`;
}
function humanize(value: string): string { return value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, character => character.toUpperCase()); }
function sourceName(source: RecommendationSource): string {
  switch (source) { case 'newOpportunity': return 'New opportunity'; case 'buyOrder': return 'Current buy order'; case 'sellListing': return 'Current sell listing'; case 'inventory': return 'Unlisted inventory'; }
}
function tone(action: RecommendationAction): string {
  if (['BUY', 'SELL', 'LIST', 'UPDATE BID'].includes(action)) return 'act';
  if (['CANCEL BID', 'REDUCE', 'REVIEW', 'SKIP'].includes(action)) return 'attention';
  return 'steady';
}
