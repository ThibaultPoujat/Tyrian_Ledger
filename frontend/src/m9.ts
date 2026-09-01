export const M9_SETTINGS_STORAGE_KEY = 'tyrian-ledger.m9.settings.v1';
export const MAXIMUM_SAFE_COPPER = 9_007_199_254_740_991n;
export const SCAN_STATUS_POLL_INTERVAL_MS = 1_000;

export type RiskProfile = 'cautious' | 'balanced' | 'adventurous';

export interface CapitalInput {
  gold: string;
  silver: string;
  copper: string;
}

export interface M9Settings {
  capital: CapitalInput;
  riskProfile: RiskProfile;
}

export interface ValidatedM9Settings extends M9Settings {
  capitalCopper: number;
}

export interface ValidationResult {
  settings?: ValidatedM9Settings;
  errors: Partial<Record<'gold' | 'silver' | 'copper' | 'riskProfile', string>>;
}

export interface ScanProgress {
  stage: string;
  finalistCount: number | null;
}

export interface ExactRoi {
  profitCopper: number;
  totalCostCopper: number;
}

export interface RouteEvidence {
  sellerQuantityAtOrBelowBuyPrice: number;
  coversSelectedQuantity: boolean;
}

export interface Recommendation {
  rank: number;
  itemId: number;
  itemName: string;
  route: 'can-act-now' | 'place-order-and-wait';
  quantity: number;
  buyUnitPriceCopper: number;
  saleUnitPriceCopper: number;
  buyOrderReserveCopper: number;
  grossSaleCopper: number;
  listingFeeCopper: number;
  exchangeFeeCopper: number;
  netSaleProceedsCopper: number;
  totalCostCopper: number;
  modeledProfitCopper: number;
  modeledRoi: ExactRoi;
  scanCompletedAtUtc: string;
  routeEvidence: RouteEvidence;
  assumptions: string[];
}

export interface CompletedScanResult {
  capitalCopper: number;
  riskProfile: RiskProfile;
  spendCapCopper: number;
  scanCompletedAtUtc: string;
  canActNow: Recommendation[];
  placeOrderAndWait: Recommendation[];
}

export type ScanState = 'idle' | 'running' | 'complete' | 'cancelled' | 'rate-limited' | 'failed';

export interface ScanSnapshot {
  state: ScanState;
  progress: ScanProgress | null;
  isRetryable: boolean;
  result: CompletedScanResult | null;
}

const riskProfiles = new Set<RiskProfile>(['cautious', 'balanced', 'adventurous']);
const scanStates = new Set<ScanState>(['idle', 'running', 'complete', 'cancelled', 'rate-limited', 'failed']);

function parseDenomination(
  value: string,
  name: string,
  maximum?: bigint,
): { value?: bigint; canonical?: string; error?: string } {
  const normalized = value.trim();
  if (normalized.length === 0) {
    return { value: 0n, canonical: '0' };
  }

  if (!/^\d+$/.test(normalized)) {
    return { error: `${name} must be a non-negative whole number.` };
  }

  const parsed = BigInt(normalized);
  if (maximum !== undefined && parsed > maximum) {
    return { error: `${name} must be between 0 and ${maximum.toString()}.` };
  }

  return { value: parsed, canonical: parsed.toString() };
}

export function validateSettings(
  capital: CapitalInput,
  riskProfile: RiskProfile | null,
): ValidationResult {
  const errors: ValidationResult['errors'] = {};
  const gold = parseDenomination(capital.gold, 'Gold');
  const silver = parseDenomination(capital.silver, 'Silver', 99n);
  const copper = parseDenomination(capital.copper, 'Copper', 99n);

  if (gold.error) errors.gold = gold.error;
  if (silver.error) errors.silver = silver.error;
  if (copper.error) errors.copper = copper.error;
  if (riskProfile === null) {
    errors.riskProfile = 'Choose the risk level that feels right for you.';
  }

  if (gold.value !== undefined && silver.value !== undefined && copper.value !== undefined) {
    const total = gold.value * 10_000n + silver.value * 100n + copper.value;
    if (total > MAXIMUM_SAFE_COPPER) {
      errors.gold = 'Your total capital is too large. Use a value within the supported limit.';
    }
  }

  if (Object.keys(errors).length > 0 || riskProfile === null ||
    gold.value === undefined || silver.value === undefined || copper.value === undefined ||
    gold.canonical === undefined || silver.canonical === undefined || copper.canonical === undefined) {
    return { errors };
  }

  const total = gold.value * 10_000n + silver.value * 100n + copper.value;
  return {
    errors,
    settings: {
      capital: {
        gold: gold.canonical,
        silver: silver.canonical,
        copper: copper.canonical,
      },
      riskProfile,
      capitalCopper: Number(total),
    },
  };
}

export function loadSettings(): ValidatedM9Settings | null {
  try {
    const raw = window.localStorage.getItem(M9_SETTINGS_STORAGE_KEY);
    if (raw === null) return null;

    const parsed: unknown = JSON.parse(raw);
    if (!isStoredSettings(parsed)) return null;
    return validateSettings(parsed.capital, parsed.riskProfile).settings ?? null;
  } catch {
    return null;
  }
}

export function saveSettings(settings: ValidatedM9Settings): void {
  const stored: M9Settings = {
    capital: settings.capital,
    riskProfile: settings.riskProfile,
  };
  window.localStorage.setItem(M9_SETTINGS_STORAGE_KEY, JSON.stringify(stored));
}

export function clearSettings(): boolean {
  try {
    window.localStorage.removeItem(M9_SETTINGS_STORAGE_KEY);
    return true;
  } catch {
    return false;
  }
}

function isStoredSettings(value: unknown): value is M9Settings {
  if (value === null || typeof value !== 'object') return false;
  const candidate = value as Partial<M9Settings>;
  return candidate.capital !== undefined &&
    typeof candidate.capital === 'object' &&
    candidate.capital !== null &&
    typeof candidate.capital.gold === 'string' &&
    typeof candidate.capital.silver === 'string' &&
    typeof candidate.capital.copper === 'string' &&
    typeof candidate.riskProfile === 'string' &&
    riskProfiles.has(candidate.riskProfile as RiskProfile);
}

function formatUnsignedCopper(value: bigint): string {
  const gold = value / 10_000n;
  const silver = (value % 10_000n) / 100n;
  const copper = value % 100n;
  return `${gold.toString()}g ${silver.toString()}s ${copper.toString()}c`;
}

export function formatCopper(value: number): string {
  const copper = BigInt(value);
  return copper < 0n
    ? `-${formatUnsignedCopper(-copper)}`
    : formatUnsignedCopper(copper);
}

export function formatModeledRoi(roi: ExactRoi): string {
  const total = BigInt(roi.totalCostCopper);
  if (total <= 0n) return '0.0%';

  const tenthsOfPercent = (BigInt(roi.profitCopper) * 1_000n) / total;
  const isNegative = tenthsOfPercent < 0n;
  const absolute = isNegative ? -tenthsOfPercent : tenthsOfPercent;
  return `${isNegative ? '-' : ''}${(absolute / 10n).toString()}.${(absolute % 10n).toString()}%`;
}

export function formatScanTime(scanCompletedAtUtc: string): string {
  const value = new Date(scanCompletedAtUtc);
  if (Number.isNaN(value.getTime())) return 'Scan time unavailable';
  return new Intl.DateTimeFormat(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    timeZoneName: 'short',
  }).format(value);
}

export const idleScanSnapshot: ScanSnapshot = {
  state: 'idle',
  progress: null,
  isRetryable: false,
  result: null,
};

async function responseSnapshot(response: Response): Promise<ScanSnapshot> {
  const payload: unknown = await response.json();
  if (!isScanSnapshot(payload)) {
    throw new Error('The scan service returned an unexpected response.');
  }

  return payload;
}

function isScanSnapshot(value: unknown): value is ScanSnapshot {
  if (value === null || typeof value !== 'object') return false;
  const candidate = value as {
    state?: unknown;
    progress?: unknown;
    isRetryable?: unknown;
    result?: unknown;
  };
  if (typeof candidate.state !== 'string' || !scanStates.has(candidate.state as ScanState) ||
    typeof candidate.isRetryable !== 'boolean' ||
    !(candidate.progress === null || isScanProgress(candidate.progress))) {
    return false;
  }

  return candidate.state === 'complete'
    ? isCompletedScanResult(candidate.result)
    : candidate.result === null;
}

function isScanProgress(value: unknown): value is ScanProgress {
  if (value === null || typeof value !== 'object') return false;
  const candidate = value as Partial<ScanProgress>;
  return typeof candidate.stage === 'string' &&
    (candidate.finalistCount === null || typeof candidate.finalistCount === 'number');
}

function isCompletedScanResult(value: unknown): value is CompletedScanResult {
  if (value === null || typeof value !== 'object') return false;
  const candidate = value as Partial<CompletedScanResult>;
  return hasNumbers(candidate, ['capitalCopper', 'spendCapCopper']) &&
    typeof candidate.riskProfile === 'string' && riskProfiles.has(candidate.riskProfile as RiskProfile) &&
    typeof candidate.scanCompletedAtUtc === 'string' &&
    Array.isArray(candidate.canActNow) && candidate.canActNow.every(isRecommendation) &&
    Array.isArray(candidate.placeOrderAndWait) && candidate.placeOrderAndWait.every(isRecommendation);
}

function isRecommendation(value: unknown): value is Recommendation {
  if (value === null || typeof value !== 'object') return false;
  const candidate = value as Partial<Recommendation>;
  return hasNumbers(candidate, [
    'rank', 'itemId', 'quantity', 'buyUnitPriceCopper', 'saleUnitPriceCopper',
    'buyOrderReserveCopper', 'grossSaleCopper', 'listingFeeCopper', 'exchangeFeeCopper',
    'netSaleProceedsCopper', 'totalCostCopper', 'modeledProfitCopper',
  ]) &&
    typeof candidate.itemName === 'string' &&
    (candidate.route === 'can-act-now' || candidate.route === 'place-order-and-wait') &&
    typeof candidate.scanCompletedAtUtc === 'string' &&
    isExactRoi(candidate.modeledRoi) &&
    isRouteEvidence(candidate.routeEvidence) &&
    Array.isArray(candidate.assumptions) && candidate.assumptions.every((assumption) => typeof assumption === 'string');
}

function isExactRoi(value: unknown): value is ExactRoi {
  return value !== null && typeof value === 'object' && hasNumbers(value, ['profitCopper', 'totalCostCopper']);
}

function isRouteEvidence(value: unknown): value is RouteEvidence {
  if (value === null || typeof value !== 'object') return false;
  const candidate = value as Partial<RouteEvidence>;
  return typeof candidate.sellerQuantityAtOrBelowBuyPrice === 'number' &&
    typeof candidate.coversSelectedQuantity === 'boolean';
}

function hasNumbers(value: object, fields: string[]): boolean {
  const candidate = value as Record<string, unknown>;
  return fields.every((field) => typeof candidate[field] === 'number' && Number.isSafeInteger(candidate[field]));
}

async function scanRequest(input: RequestInfo | URL, init?: RequestInit): Promise<ScanSnapshot> {
  const response = init === undefined ? await fetch(input) : await fetch(input, init);
  const snapshot = await responseSnapshot(response);
  if (!response.ok && response.status !== 409) {
    throw new Error('The scan service could not process that request.');
  }

  return snapshot;
}

export function startScan(settings: ValidatedM9Settings): Promise<ScanSnapshot> {
  return scanRequest('/api/recommendations/scan', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      capitalCopper: settings.capitalCopper,
      riskProfile: settings.riskProfile,
    }),
  });
}

export function getScanStatus(): Promise<ScanSnapshot> {
  return scanRequest('/api/recommendations/scan');
}

export function cancelScan(): Promise<ScanSnapshot> {
  return scanRequest('/api/recommendations/scan', { method: 'DELETE' });
}
