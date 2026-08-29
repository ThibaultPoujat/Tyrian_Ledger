export type DashboardConfidence = 'normal' | 'reduced';
export type DashboardFreshness = 'current' | 'stale';
export type DashboardEffortCategory = 'very-low' | 'low' | 'medium' | 'high' | 'ongoing-patient';
export type DashboardRiskPreference = 'all' | DashboardConfidence;
export type DashboardStrategyPreference = 'all' | 'market-flip';

export interface UserSessionPreferences {
  capitalLimitCopper: number | null;
  minimumProfitCopper: number | null;
  riskPreference: DashboardRiskPreference;
  strategyPreference: DashboardStrategyPreference;
  allocationPercent: number;
}

export type AccountAccessValidationStatus = 'notconfigured' | 'valid' | 'invalid' | 'unavailable';

export interface AccountFeatureAccess {
  feature: 'account-materials' | 'account-crafting';
  isAvailable: boolean;
  missingPermissions: string[];
}

export interface AccountAccessStatus {
  validationStatus: AccountAccessValidationStatus;
  keyId: string | null;
  keyName: string | null;
  permissions: string[];
  features: AccountFeatureAccess[];
}

export interface OperationProfitStatistics {
  eligibleOperationCount: number;
  totalCopper: number | null;
}

export interface OperationLifecycleStatistics {
  completedOperationCount: number;
  cancelledOperationCount: number;
  terminalOperationCount: number;
}

export interface OperationHistoryStatistics {
  operationCount: number;
  firstRecordedAtUtc: string | null;
  lastRecordedAtUtc: string | null;
  modeledNetProfit: OperationProfitStatistics;
  realizedProfit: OperationProfitStatistics;
  lifecycle: OperationLifecycleStatistics;
}

export interface MarketResearchCoverage {
  observationCount: number;
  firstCapturedAtUtc: string | null;
  lastCapturedAtUtc: string | null;
}

export interface MarketResearchPriceStatistics {
  observationCount: number;
  tenthPercentileCopper: number | null;
  medianCopper: number | null;
  ninetiethPercentileCopper: number | null;
}

export interface MarketResearchLiquidity {
  observationCount: number;
  coefficientOfVariationPercent: number | null;
}

export interface MarketResearchWatchlistItem {
  itemId: number;
  coverage: MarketResearchCoverage;
  buyPrices: MarketResearchPriceStatistics;
  sellPrices: MarketResearchPriceStatistics;
  buyLiquidity: MarketResearchLiquidity;
  sellLiquidity: MarketResearchLiquidity;
}

export interface MarketResearchWatchlist {
  maximumTrackedItemCount: number;
  trackedItemCount: number;
  items: MarketResearchWatchlistItem[];
}

export interface DashboardOpportunity {
  itemId: number;
  label: string;
  strategy: string;
  effortCategory: DashboardEffortCategory;
  rank: number;
  scoreBasisPoints: number;
  capitalRequiredCopper: number;
  modeledNetProfitCopper: number;
  returnOnInvestmentBasisPoints: number;
  liquidityPriceImpactCopper: number;
  confidence: DashboardConfidence;
  freshness: DashboardFreshness;
  capturedAtUtc: string;
  detail: DashboardOpportunityDetail;
}

export interface DashboardOpportunityDetail {
  requestedQuantity: number;
  analyzedAtUtc: string;
  acquisition: DashboardExecution;
  exit: DashboardExecution;
  fees: DashboardFees;
  financials: DashboardFinancials;
  liquidity: DashboardLiquidity;
  freshness: DashboardFreshness;
  capturedAtUtc: string;
  expiresAtUtc: string;
  confidence: DashboardConfidence;
}

export interface DashboardExecution {
  requestedQuantity: number;
  filledQuantity: number;
  isFullyFilled: boolean;
  totalValueCopper: number;
  priceImpactCopper: number;
}

export interface DashboardFees {
  listingBasisPoints: number;
  listingRounding: 'down' | 'up';
  listingFeeCopper: number;
  exchangeBasisPoints: number;
  exchangeRounding: 'down' | 'up';
  exchangeFeeCopper: number;
}

export interface DashboardFinancials {
  acquisitionCostCopper: number;
  grossSaleValueCopper: number;
  netSaleProceedsCopper: number;
  capitalRequiredCopper: number;
  modeledNetProfitCopper: number;
  returnOnInvestmentBasisPoints: number;
}

export interface DashboardLiquidity {
  acquisitionFilledQuantity: number;
  liquidationFilledQuantity: number;
  isFullyAcquirable: boolean;
  isFullyLiquidatable: boolean;
  acquisitionPriceImpactCopper: number;
  liquidationPriceImpactCopper: number;
  totalPriceImpactCopper: number;
}

export interface DashboardOpportunitiesResponse {
  isSampleData: boolean;
  sourceDescription: string;
  generatedAtUtc: string;
  opportunities: DashboardOpportunity[];
}

export async function loadDashboardOpportunities(
  signal: AbortSignal,
  effortCategory?: DashboardEffortCategory,
): Promise<DashboardOpportunitiesResponse> {
  const path = effortCategory === undefined
    ? '/api/dashboard/opportunities'
    : `/api/dashboard/opportunities?effortCategory=${encodeURIComponent(effortCategory)}`;
  const response = await fetch(path, { signal });
  if (!response.ok) {
    throw new Error(`Dashboard request failed with status ${response.status}.`);
  }

  const payload: unknown = await response.json();
  if (!isDashboardOpportunitiesResponse(payload)) {
    throw new Error('Dashboard response was not in the expected format.');
  }

  return payload;
}

export async function loadUserSessionPreferences(
  signal: AbortSignal,
): Promise<UserSessionPreferences> {
  const response = await fetch('/api/preferences/user-session', { signal });
  if (!response.ok) {
    throw new Error(`Preference request failed with status ${response.status}.`);
  }

  const payload: unknown = await response.json();
  if (!isUserSessionPreferences(payload)) {
    throw new Error('Preference response was not in the expected format.');
  }

  return payload;
}

export async function loadAccountAccessStatus(signal: AbortSignal): Promise<AccountAccessStatus> {
  const response = await fetch('/api/account/access', { signal });
  if (!response.ok) {
    throw new Error(`Account access request failed with status ${response.status}.`);
  }

  const payload: unknown = await response.json();
  if (!isAccountAccessStatus(payload)) {
    throw new Error('Account access response was not in the expected format.');
  }

  return payload;
}

export async function loadOperationHistoryStatistics(signal: AbortSignal): Promise<OperationHistoryStatistics> {
  const response = await fetch('/api/history/statistics', { signal });
  if (!response.ok) {
    throw new Error(`Operation history statistics request failed with status ${response.status}.`);
  }

  const payload: unknown = await response.json();
  if (!isOperationHistoryStatistics(payload)) {
    throw new Error('Operation history statistics response was not in the expected format.');
  }

  return payload;
}

export async function loadMarketResearchWatchlist(signal: AbortSignal): Promise<MarketResearchWatchlist> {
  const response = await fetch('/api/market-research/watchlist', { signal });
  if (!response.ok) {
    throw new Error(`Market research request failed with status ${response.status}.`);
  }

  const payload: unknown = await response.json();
  if (!isMarketResearchWatchlist(payload)) {
    throw new Error('Market research response was not in the expected format.');
  }

  return payload;
}

export async function addMarketResearchWatchlistItem(itemId: number): Promise<void> {
  const response = await fetch('/api/market-research/watchlist', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ itemId }),
  });
  if (!response.ok) {
    throw new Error(`Adding the research item failed with status ${response.status}.`);
  }
}

export async function removeMarketResearchWatchlistItem(itemId: number): Promise<void> {
  const response = await fetch(`/api/market-research/watchlist/${encodeURIComponent(itemId)}`, {
    method: 'DELETE',
  });
  if (!response.ok) {
    throw new Error(`Removing the research item failed with status ${response.status}.`);
  }
}

export async function clearAccountSnapshotData(): Promise<void> {
  const response = await fetch('/api/account/snapshots', { method: 'DELETE' });
  if (!response.ok) {
    throw new Error(`Account snapshot clearing failed with status ${response.status}.`);
  }
}

export async function saveUserSessionPreferences(
  preferences: UserSessionPreferences,
): Promise<UserSessionPreferences> {
  const response = await fetch('/api/preferences/user-session', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(preferences),
  });

  if (!response.ok) {
    throw new Error(`Preference save failed with status ${response.status}.`);
  }

  const payload: unknown = await response.json();
  if (!isUserSessionPreferences(payload)) {
    throw new Error('Preference save response was not in the expected format.');
  }

  return payload;
}

function isDashboardOpportunitiesResponse(value: unknown): value is DashboardOpportunitiesResponse {
  if (!isRecord(value)
    || typeof value.isSampleData !== 'boolean'
    || typeof value.sourceDescription !== 'string'
    || typeof value.generatedAtUtc !== 'string'
    || !Array.isArray(value.opportunities)) {
    return false;
  }

  return value.opportunities.every(isDashboardOpportunity);
}

function isUserSessionPreferences(value: unknown): value is UserSessionPreferences {
  return isRecord(value)
    && isNullableSafeInteger(value.capitalLimitCopper)
    && isNullableSafeInteger(value.minimumProfitCopper)
    && (value.riskPreference === 'all' || value.riskPreference === 'normal' || value.riskPreference === 'reduced')
    && (value.strategyPreference === 'all'
      || value.strategyPreference === 'market-flip')
    && typeof value.allocationPercent === 'number'
    && Number.isSafeInteger(value.allocationPercent)
    && value.allocationPercent >= 1
    && value.allocationPercent <= 100;
}

function isAccountAccessStatus(value: unknown): value is AccountAccessStatus {
  return isRecord(value)
    && (value.validationStatus === 'notconfigured'
      || value.validationStatus === 'valid'
      || value.validationStatus === 'invalid'
      || value.validationStatus === 'unavailable')
    && (typeof value.keyId === 'string' || value.keyId === null)
    && (typeof value.keyName === 'string' || value.keyName === null)
    && Array.isArray(value.permissions)
    && value.permissions.every((permission) => typeof permission === 'string')
    && Array.isArray(value.features)
    && value.features.every(isAccountFeatureAccess);
}

function isAccountFeatureAccess(value: unknown): value is AccountFeatureAccess {
  return isRecord(value)
    && (value.feature === 'account-materials' || value.feature === 'account-crafting')
    && typeof value.isAvailable === 'boolean'
    && Array.isArray(value.missingPermissions)
    && value.missingPermissions.every((permission) => typeof permission === 'string');
}

function isOperationHistoryStatistics(value: unknown): value is OperationHistoryStatistics {
  return isRecord(value)
    && isNonNegativeSafeInteger(value.operationCount)
    && isNullableString(value.firstRecordedAtUtc)
    && isNullableString(value.lastRecordedAtUtc)
    && isOperationProfitStatistics(value.modeledNetProfit)
    && isOperationProfitStatistics(value.realizedProfit)
    && isOperationLifecycleStatistics(value.lifecycle);
}

function isMarketResearchWatchlist(value: unknown): value is MarketResearchWatchlist {
  return isRecord(value)
    && isNonNegativeSafeInteger(value.maximumTrackedItemCount)
    && isNonNegativeSafeInteger(value.trackedItemCount)
    && value.trackedItemCount <= value.maximumTrackedItemCount
    && Array.isArray(value.items)
    && value.items.length <= value.trackedItemCount
    && value.items.every(isMarketResearchWatchlistItem);
}

function isMarketResearchWatchlistItem(value: unknown): value is MarketResearchWatchlistItem {
  return isRecord(value)
    && isPositiveSafeInteger(value.itemId)
    && isMarketResearchCoverage(value.coverage)
    && isMarketResearchPriceStatistics(value.buyPrices)
    && isMarketResearchPriceStatistics(value.sellPrices)
    && isMarketResearchLiquidity(value.buyLiquidity)
    && isMarketResearchLiquidity(value.sellLiquidity);
}

function isMarketResearchCoverage(value: unknown): value is MarketResearchCoverage {
  return isRecord(value)
    && isNonNegativeSafeInteger(value.observationCount)
    && isNullableString(value.firstCapturedAtUtc)
    && isNullableString(value.lastCapturedAtUtc)
    && (value.observationCount === 0
      ? value.firstCapturedAtUtc === null && value.lastCapturedAtUtc === null
      : typeof value.firstCapturedAtUtc === 'string' && typeof value.lastCapturedAtUtc === 'string');
}

function isMarketResearchPriceStatistics(value: unknown): value is MarketResearchPriceStatistics {
  return isRecord(value)
    && isNonNegativeSafeInteger(value.observationCount)
    && isNullableSafeInteger(value.tenthPercentileCopper)
    && isNullableSafeInteger(value.medianCopper)
    && isNullableSafeInteger(value.ninetiethPercentileCopper);
}

function isMarketResearchLiquidity(value: unknown): value is MarketResearchLiquidity {
  return isRecord(value)
    && isNonNegativeSafeInteger(value.observationCount)
    && isNullableNonNegativeNumber(value.coefficientOfVariationPercent);
}

function isOperationProfitStatistics(value: unknown): value is OperationProfitStatistics {
  return isRecord(value)
    && isNonNegativeSafeInteger(value.eligibleOperationCount)
    && isNullableSignedSafeInteger(value.totalCopper)
    && (value.eligibleOperationCount === 0 ? value.totalCopper === null : value.totalCopper !== null);
}

function isOperationLifecycleStatistics(value: unknown): value is OperationLifecycleStatistics {
  return isRecord(value)
    && isNonNegativeSafeInteger(value.completedOperationCount)
    && isNonNegativeSafeInteger(value.cancelledOperationCount)
    && isNonNegativeSafeInteger(value.terminalOperationCount)
    && value.terminalOperationCount === value.completedOperationCount + value.cancelledOperationCount;
}

function isDashboardOpportunity(value: unknown): value is DashboardOpportunity {
  return isRecord(value)
    && typeof value.itemId === 'number'
    && typeof value.label === 'string'
    && typeof value.strategy === 'string'
    && isDashboardEffortCategory(value.effortCategory)
    && typeof value.rank === 'number'
    && typeof value.scoreBasisPoints === 'number'
    && typeof value.capitalRequiredCopper === 'number'
    && typeof value.modeledNetProfitCopper === 'number'
    && typeof value.returnOnInvestmentBasisPoints === 'number'
    && typeof value.liquidityPriceImpactCopper === 'number'
    && (value.confidence === 'normal' || value.confidence === 'reduced')
    && (value.freshness === 'current' || value.freshness === 'stale')
    && typeof value.capturedAtUtc === 'string'
    && isDashboardOpportunityDetail(value.detail);
}

function isDashboardEffortCategory(value: unknown): value is DashboardEffortCategory {
  return value === 'very-low'
    || value === 'low'
    || value === 'medium'
    || value === 'high'
    || value === 'ongoing-patient';
}

function isDashboardOpportunityDetail(value: unknown): value is DashboardOpportunityDetail {
  return isRecord(value)
    && typeof value.requestedQuantity === 'number'
    && typeof value.analyzedAtUtc === 'string'
    && isDashboardExecution(value.acquisition)
    && isDashboardExecution(value.exit)
    && isDashboardFees(value.fees)
    && isDashboardFinancials(value.financials)
    && isDashboardLiquidity(value.liquidity)
    && (value.freshness === 'current' || value.freshness === 'stale')
    && typeof value.capturedAtUtc === 'string'
    && typeof value.expiresAtUtc === 'string'
    && (value.confidence === 'normal' || value.confidence === 'reduced');
}

function isDashboardExecution(value: unknown): value is DashboardExecution {
  return isRecord(value)
    && typeof value.requestedQuantity === 'number'
    && typeof value.filledQuantity === 'number'
    && typeof value.isFullyFilled === 'boolean'
    && typeof value.totalValueCopper === 'number'
    && typeof value.priceImpactCopper === 'number';
}

function isDashboardFees(value: unknown): value is DashboardFees {
  return isRecord(value)
    && typeof value.listingBasisPoints === 'number'
    && (value.listingRounding === 'down' || value.listingRounding === 'up')
    && typeof value.listingFeeCopper === 'number'
    && typeof value.exchangeBasisPoints === 'number'
    && (value.exchangeRounding === 'down' || value.exchangeRounding === 'up')
    && typeof value.exchangeFeeCopper === 'number';
}

function isDashboardFinancials(value: unknown): value is DashboardFinancials {
  return isRecord(value)
    && typeof value.acquisitionCostCopper === 'number'
    && typeof value.grossSaleValueCopper === 'number'
    && typeof value.netSaleProceedsCopper === 'number'
    && typeof value.capitalRequiredCopper === 'number'
    && typeof value.modeledNetProfitCopper === 'number'
    && typeof value.returnOnInvestmentBasisPoints === 'number';
}

function isDashboardLiquidity(value: unknown): value is DashboardLiquidity {
  return isRecord(value)
    && typeof value.acquisitionFilledQuantity === 'number'
    && typeof value.liquidationFilledQuantity === 'number'
    && typeof value.isFullyAcquirable === 'boolean'
    && typeof value.isFullyLiquidatable === 'boolean'
    && typeof value.acquisitionPriceImpactCopper === 'number'
    && typeof value.liquidationPriceImpactCopper === 'number'
    && typeof value.totalPriceImpactCopper === 'number';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function isNullableSafeInteger(value: unknown): value is number | null {
  return value === null || (typeof value === 'number' && Number.isSafeInteger(value) && value >= 0);
}

function isNullableSignedSafeInteger(value: unknown): value is number | null {
  return value === null || (typeof value === 'number' && Number.isSafeInteger(value));
}

function isNullableString(value: unknown): value is string | null {
  return typeof value === 'string' || value === null;
}

function isNonNegativeSafeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}

function isPositiveSafeInteger(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
}

function isNullableNonNegativeNumber(value: unknown): value is number | null {
  return value === null || (typeof value === 'number' && Number.isFinite(value) && value >= 0);
}
