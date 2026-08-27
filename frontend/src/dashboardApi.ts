export type DashboardConfidence = 'normal' | 'reduced';
export type DashboardFreshness = 'current' | 'stale';

export interface DashboardOpportunity {
  itemId: number;
  label: string;
  strategy: string;
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
): Promise<DashboardOpportunitiesResponse> {
  const response = await fetch('/api/dashboard/opportunities', { signal });
  if (!response.ok) {
    throw new Error(`Dashboard request failed with status ${response.status}.`);
  }

  const payload: unknown = await response.json();
  if (!isDashboardOpportunitiesResponse(payload)) {
    throw new Error('Dashboard response was not in the expected format.');
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

function isDashboardOpportunity(value: unknown): value is DashboardOpportunity {
  return isRecord(value)
    && typeof value.itemId === 'number'
    && typeof value.label === 'string'
    && typeof value.strategy === 'string'
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
