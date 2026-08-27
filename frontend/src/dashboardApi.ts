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
    && typeof value.capturedAtUtc === 'string';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
