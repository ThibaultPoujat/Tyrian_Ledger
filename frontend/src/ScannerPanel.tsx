import { useEffect, useMemo, useRef, useState } from 'react';

type Money = { copper: string };
type Level = { listings: number; quantity: number; unitPrice: Money };
type Candidate = {
  itemId: number;
  itemName: string;
  bestBuy: { quantity: number; unitPrice: Money };
  lowestSell: { quantity: number; unitPrice: Money };
  plannedBid: Money;
  plannedListPrice: Money;
  netProfit: Money;
  totalCost: Money;
  maximumBid: Money;
  modeledRoi: { profit: Money; totalCost: Money; displayPercent: string };
  liquidity: {
    participationCapQuantity: number;
    reasons: string[];
    acquisition: { requestedQuantity: number; filledQuantity: number; isFullyFilled: boolean; totalValue: Money };
    liquidation: { requestedQuantity: number; filledQuantity: number; isFullyFilled: boolean; totalValue: Money };
    topBuyLevels: Level[];
    topSellLevels: Level[];
  };
};
type Scanner = {
  state: 'ready' | 'unavailable'; error: string | null; observedAtUtc: string | null;
  isFeeRoundingExternallyVerified: boolean; isTruncated: boolean; candidates: Candidate[];
  exclusions: Array<{ reason: string; count: number }>;
};
type Status = 'loading' | 'ready' | 'error';

const headers = { Accept: 'application/json', 'X-Tyrian-Ledger-Request': '1' };
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null;
const integer = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const money = (value: unknown): value is Money => record(value) && typeof value.copper === 'string' && /^-?\d+$/.test(value.copper);
const levels = (value: unknown): value is Level[] => Array.isArray(value) && value.every(level => record(level) && integer(level.listings) && integer(level.quantity) && money(level.unitPrice));
const candidate = (value: unknown): value is Candidate => record(value) && integer(value.itemId) && typeof value.itemName === 'string' && record(value.bestBuy) && integer(value.bestBuy.quantity) && money(value.bestBuy.unitPrice) && record(value.lowestSell) && integer(value.lowestSell.quantity) && money(value.lowestSell.unitPrice) && money(value.plannedBid) && money(value.plannedListPrice) && money(value.netProfit) && money(value.totalCost) && money(value.maximumBid) && record(value.modeledRoi) && money(value.modeledRoi.profit) && money(value.modeledRoi.totalCost) && typeof value.modeledRoi.displayPercent === 'string' && record(value.liquidity) && integer(value.liquidity.participationCapQuantity) && Array.isArray(value.liquidity.reasons) && value.liquidity.reasons.every(reason => typeof reason === 'string') && record(value.liquidity.acquisition) && integer(value.liquidity.acquisition.requestedQuantity) && integer(value.liquidity.acquisition.filledQuantity) && typeof value.liquidity.acquisition.isFullyFilled === 'boolean' && money(value.liquidity.acquisition.totalValue) && record(value.liquidity.liquidation) && integer(value.liquidity.liquidation.requestedQuantity) && integer(value.liquidity.liquidation.filledQuantity) && typeof value.liquidity.liquidation.isFullyFilled === 'boolean' && money(value.liquidity.liquidation.totalValue) && levels(value.liquidity.topBuyLevels) && levels(value.liquidity.topSellLevels);
const scanner = (value: unknown): value is Scanner => record(value) && (value.state === 'ready' || value.state === 'unavailable') && (value.error === null || typeof value.error === 'string') && (value.observedAtUtc === null || typeof value.observedAtUtc === 'string') && typeof value.isFeeRoundingExternallyVerified === 'boolean' && typeof value.isTruncated === 'boolean' && Array.isArray(value.candidates) && value.candidates.every(candidate) && Array.isArray(value.exclusions) && value.exclusions.every(exclusion => record(exclusion) && typeof exclusion.reason === 'string' && integer(exclusion.count));
const watchlist = (value: unknown): value is { itemIds: number[] } => record(value) && Array.isArray(value.itemIds) && value.itemIds.every(integer);

function copper(value: Money): string {
  const raw = BigInt(value.copper); const sign = raw < 0n ? '−' : ''; const absolute = raw < 0n ? -raw : raw;
  return `${sign}${absolute / 10000n}g ${(absolute % 10000n) / 100n}s ${absolute % 100n}c`;
}
function reason(value: string): string { return value.replace(/([A-Z])/g, ' $1').replace(/^./, letter => letter.toUpperCase()); }

export default function ScannerPanel({ watchlistRefreshGeneration = 0 }: { watchlistRefreshGeneration?: number }) {
  const [status, setStatus] = useState<Status>('loading');
  const [result, setResult] = useState<Scanner | null>(null);
  const [watching, setWatching] = useState<number[]>([]);
  const [watchlistStatus, setWatchlistStatus] = useState<Status>('loading');
  const [updating, setUpdating] = useState<number | null>(null);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [minimumRoi, setMinimumRoi] = useState('0'); const [minimumProfit, setMinimumProfit] = useState('1'); const [quantity, setQuantity] = useState('1');
  const [minPrice, setMinPrice] = useState(''); const [maxPrice, setMaxPrice] = useState(''); const [maxCapital, setMaxCapital] = useState('');
  const [liquidityOnly, setLiquidityOnly] = useState(false); const [sort, setSort] = useState<'profit' | 'roi' | 'maxBid' | 'liquidity'>('profit');
  const scannerRequest = useRef(0); const watchlistRequest = useRef(0);

  const loadWatchlist = () => {
    const generation = ++watchlistRequest.current;
    setWatchlistStatus('loading');
    return fetch('/api/watchlist', { headers }).then(async response => {
      const body: unknown = await response.json();
      if (!response.ok || !watchlist(body)) throw new Error('Invalid watchlist response');
      if (generation !== watchlistRequest.current) return;
      setWatching(body.itemIds);
      setWatchlistStatus('ready');
    }).catch(error => {
      if (generation === watchlistRequest.current) setWatchlistStatus('error');
      throw error;
    });
  };
  const load = () => {
    const generation = ++scannerRequest.current;
    setStatus('loading');
    const query = new URLSearchParams({ minimumRoiBasisPoints: minimumRoi, minimumNetProfitCopper: minimumProfit, intendedQuantity: quantity });
    fetch(`/api/live-market-scanner?${query}`, { headers }).then(async response => {
      const body: unknown = await response.json();
      if (generation !== scannerRequest.current) return;
      if (!response.ok || !scanner(body) || body.state !== 'ready') throw new Error('Invalid scanner response');
      setResult(body); setStatus('ready');
    }).catch(() => { if (generation === scannerRequest.current) setStatus('error'); });
  };
  useEffect(() => {
    load();
    return () => { scannerRequest.current++; };
  }, []);

  useEffect(() => {
    void loadWatchlist().catch(() => undefined);
    return () => { watchlistRequest.current++; };
  }, [watchlistRefreshGeneration]);

  const presentationFiltersValid = [minPrice, maxPrice, maxCapital].every(value => value === '' || /^\d+$/.test(value));
  const visible = useMemo(() => {
    if (result === null || !presentationFiltersValid) return [];
    const minimum = minPrice === '' ? null : BigInt(minPrice); const maximum = maxPrice === '' ? null : BigInt(maxPrice); const capital = maxCapital === '' ? null : BigInt(maxCapital);
    return result.candidates.filter(item => (minimum === null || BigInt(item.plannedBid.copper) >= minimum) && (maximum === null || BigInt(item.plannedBid.copper) <= maximum) && (capital === null || BigInt(item.totalCost.copper) <= capital) && (!liquidityOnly || item.liquidity.reasons.length === 0)).sort((left, right) => {
      if (sort === 'liquidity') return left.liquidity.reasons.length - right.liquidity.reasons.length || left.itemName.localeCompare(right.itemName);
      if (sort === 'maxBid') return BigInt(right.maximumBid.copper) > BigInt(left.maximumBid.copper) ? 1 : BigInt(right.maximumBid.copper) < BigInt(left.maximumBid.copper) ? -1 : left.itemName.localeCompare(right.itemName);
      if (sort === 'roi') { const a = BigInt(left.modeledRoi.profit.copper) * BigInt(right.modeledRoi.totalCost.copper); const b = BigInt(right.modeledRoi.profit.copper) * BigInt(left.modeledRoi.totalCost.copper); return a > b ? -1 : a < b ? 1 : left.itemName.localeCompare(right.itemName); }
      return BigInt(right.netProfit.copper) > BigInt(left.netProfit.copper) ? 1 : BigInt(right.netProfit.copper) < BigInt(left.netProfit.copper) ? -1 : left.itemName.localeCompare(right.itemName);
    });
  }, [result, minPrice, maxPrice, maxCapital, liquidityOnly, sort, presentationFiltersValid]);
  const toggle = (itemId: number, isWatching: boolean) => {
    setMutationError(null); setUpdating(itemId);
    fetch(`/api/watchlist/${itemId}`, { method: isWatching ? 'DELETE' : 'PUT', headers })
      .then(response => { if (!response.ok) throw new Error('Watchlist update failed'); return loadWatchlist(); })
      .catch(() => setMutationError('The watchlist change could not be confirmed. Please retry.'))
      .finally(() => setUpdating(null));
  };
  const scannerFiltersValid = /^\d+$/.test(minimumRoi) && /^\d+$/.test(minimumProfit) && /^[1-9]\d*$/.test(quantity);

  return <section aria-labelledby="scanner-title" className="scanner-panel">
    <div className="dashboard-heading"><div><p className="eyebrow">Live market intelligence</p><h2 id="scanner-title">Current scanner and watchlist</h2></div><button className="sync-button" disabled={!scannerFiltersValid || status === 'loading'} onClick={load} type="button">{status === 'loading' ? 'Scanning…' : 'Refresh scanner'}</button></div>
    <p>Values are calculated by the local host from current market evidence; they are not fill or profit guarantees.</p>
    <fieldset className="scanner-filters"><legend>Scanner controls</legend>
      <label>Minimum ROI (basis points)<input aria-label="Minimum ROI (basis points)" value={minimumRoi} onChange={event => setMinimumRoi(event.target.value)} inputMode="numeric" /></label>
      <label>Minimum modeled profit (copper)<input aria-label="Minimum modeled profit (copper)" value={minimumProfit} onChange={event => setMinimumProfit(event.target.value)} inputMode="numeric" /></label>
      <label>Intended quantity<input aria-label="Intended quantity" value={quantity} onChange={event => setQuantity(event.target.value)} inputMode="numeric" /></label>
      <label>Min planned bid (copper)<input aria-invalid={minPrice !== '' && !/^\d+$/.test(minPrice)} value={minPrice} onChange={event => setMinPrice(event.target.value)} inputMode="numeric" /></label>
      <label>Max planned bid (copper)<input aria-label="Max planned bid (copper)" aria-invalid={maxPrice !== '' && !/^\d+$/.test(maxPrice)} value={maxPrice} onChange={event => setMaxPrice(event.target.value)} inputMode="numeric" /></label>
      <label>Max per-unit capital (copper)<input aria-invalid={maxCapital !== '' && !/^\d+$/.test(maxCapital)} value={maxCapital} onChange={event => setMaxCapital(event.target.value)} inputMode="numeric" /></label>
      <label>Sort<select value={sort} onChange={event => setSort(event.target.value as typeof sort)}><option value="profit">Modeled profit</option><option value="roi">Modeled ROI</option><option value="maxBid">Maximum bid</option><option value="liquidity">Fewest liquidity flags</option></select></label>
      <label className="check"><input checked={liquidityOnly} onChange={event => setLiquidityOnly(event.target.checked)} type="checkbox" />No liquidity flags only</label>
    </fieldset>
    {!presentationFiltersValid && <p role="alert">Price and capital filters must be whole copper values.</p>}
    {status === 'loading' && <p aria-busy="true" role="status">Loading current scanner evidence…</p>}
    {status === 'error' && <p role="alert">Current scanner evidence is unavailable. Try again shortly.</p>}
    <Watchlist
      status={watchlistStatus}
      itemIds={watching}
      updatingItemId={updating}
      onToggle={toggle}
      onRetry={() => void loadWatchlist().catch(() => undefined)} />
    {mutationError !== null && <p role="alert">{mutationError}</p>}
    {status === 'ready' && result !== null && <>
      <p className="notice">Observed: {result.observedAtUtc === null ? 'Unavailable' : new Date(result.observedAtUtc).toLocaleString()}. {!result.isFeeRoundingExternallyVerified && 'Fee rounding remains modeled and provisional.'}</p>
      {result.isTruncated && <p className="notice">Only the bounded current scanner shortlist is displayed; broader market matches may not be shown.</p>}
      {result.exclusions.length > 0 && <p className="notice">Not shown: {result.exclusions.map(item => `${item.count} ${reason(item.reason)}`).join('; ')}.</p>}
      {visible.length === 0 ? <p role="status">{result.isTruncated ? 'No displayed candidates match these presentation filters.' : 'No current candidates match these presentation filters.'}</p> : <div className="scanner-candidates">{visible.map(item => {
        const isWatching = watching.includes(item.itemId);
        return <article className="scanner-candidate" key={item.itemId}><div className="scanner-heading"><h3>{item.itemName}</h3><button disabled={updating === item.itemId} onClick={() => toggle(item.itemId, isWatching)} type="button">{isWatching ? 'Remove from watchlist' : 'Add to watchlist'}</button></div>
          <dl><div><dt>Current buy</dt><dd>{copper(item.bestBuy.unitPrice)} ({item.bestBuy.quantity})</dd></div><div><dt>Current sell</dt><dd>{copper(item.lowestSell.unitPrice)} ({item.lowestSell.quantity})</dd></div><div><dt>Planned bid</dt><dd>{copper(item.plannedBid)}</dd></div><div><dt>Planned list</dt><dd>{copper(item.plannedListPrice)}</dd></div><div><dt>Modeled net profit</dt><dd>{copper(item.netProfit)}</dd></div><div><dt>Modeled ROI</dt><dd>{item.modeledRoi.displayPercent}</dd></div><div><dt>Maximum bid</dt><dd>{copper(item.maximumBid)}</dd></div><div><dt>Depth participation cap</dt><dd>{item.liquidity.participationCapQuantity}</dd></div><div><dt>Requested acquisition capital</dt><dd>{item.liquidity.acquisition.isFullyFilled ? copper(item.liquidity.acquisition.totalValue) : 'Insufficient visible depth'}</dd></div></dl>
          {item.liquidity.reasons.length === 0 ? <p>Visible depth has no current liquidity flags.</p> : <p className="risk-flags">Risk: {item.liquidity.reasons.map(reason).join('; ')}.</p>}
          <details><summary>Order-book detail</summary><p>Top ten visible price levels per side from this scan.</p><div className="dashboard-split"><OrderLevels title="Buy orders" levels={item.liquidity.topBuyLevels} /><OrderLevels title="Sell orders" levels={item.liquidity.topSellLevels} /></div></details>
        </article>;
      })}</div>}
    </>}
  </section>;
}

function Watchlist({ status, itemIds, updatingItemId, onToggle, onRetry }: { status: Status; itemIds: number[]; updatingItemId: number | null; onToggle: (itemId: number, isWatching: boolean) => void; onRetry: () => void }) {
  return <section className="watchlist-summary" aria-label="Watchlist"><h3>Local watchlist</h3>
    {status === 'loading' ? <p role="status">Loading watchlist…</p> : status === 'error' ? <p role="alert">Watchlist unavailable. <button onClick={onRetry} type="button">Retry</button></p> : itemIds.length === 0 ? <p>No watched markets yet.</p> : <ul>{itemIds.map(itemId => <li key={itemId}>Item #{itemId} <button disabled={updatingItemId === itemId} onClick={() => onToggle(itemId, true)} type="button">Remove</button></li>)}</ul>}
  </section>;
}

function OrderLevels({ title, levels }: { title: string; levels: Level[] }) {
  return <section className="dashboard-items"><h3>{title}</h3>{levels.length === 0 ? <p>No visible levels.</p> : <table><thead><tr><th>Price</th><th>Qty</th><th>Listings</th></tr></thead><tbody>{levels.map((level, index) => <tr key={`${level.unitPrice.copper}-${index}`}><td>{copper(level.unitPrice)}</td><td>{level.quantity}</td><td>{level.listings}</td></tr>)}</tbody></table>}</section>;
}
