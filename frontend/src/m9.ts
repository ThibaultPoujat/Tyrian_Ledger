export const M9_SETTINGS_STORAGE_KEY = 'tyrian-ledger.m9.settings.v1';
export const MAXIMUM_SAFE_COPPER = 9_007_199_254_740_991n;

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
  capitalCopper: bigint;
}

export interface ValidationResult {
  settings?: ValidatedM9Settings;
  errors: Partial<Record<'gold' | 'silver' | 'copper' | 'riskProfile', string>>;
}

export interface ExactRoi {
  profitCopper: bigint;
  totalCostCopper: bigint;
}

const riskProfiles = new Set<RiskProfile>(['cautious', 'balanced', 'adventurous']);

function parseDenomination(
  value: string,
  name: string,
  maximum?: bigint,
): { value?: bigint; canonical?: string; error?: string } {
  const normalized = value.trim();
  if (normalized.length === 0) return { value: 0n, canonical: '0' };
  if (!/^\d+$/.test(normalized)) return { error: `${name} must be a non-negative whole number.` };

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
  if (riskProfile === null) errors.riskProfile = 'Choose the risk level that feels right for you.';

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

  return {
    errors,
    settings: {
      capital: { gold: gold.canonical, silver: silver.canonical, copper: copper.canonical },
      riskProfile,
      capitalCopper: gold.value * 10_000n + silver.value * 100n + copper.value,
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
  const stored: M9Settings = { capital: settings.capital, riskProfile: settings.riskProfile };
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
  return candidate.capital !== undefined && typeof candidate.capital === 'object' && candidate.capital !== null &&
    typeof candidate.capital.gold === 'string' && typeof candidate.capital.silver === 'string' &&
    typeof candidate.capital.copper === 'string' && typeof candidate.riskProfile === 'string' &&
    riskProfiles.has(candidate.riskProfile as RiskProfile);
}

function formatUnsignedCopper(value: bigint): string {
  const gold = value / 10_000n;
  const silver = (value % 10_000n) / 100n;
  const copper = value % 100n;
  return `${gold.toString()}g ${silver.toString()}s ${copper.toString()}c`;
}

export function formatCopper(value: bigint): string {
  return value < 0n ? `-${formatUnsignedCopper(-value)}` : formatUnsignedCopper(value);
}

export function formatModeledRoi(roi: ExactRoi): string {
  if (roi.totalCostCopper <= 0n) return '0.0%';

  const tenthsOfPercent = (roi.profitCopper * 1_000n) / roi.totalCostCopper;
  const isNegative = tenthsOfPercent < 0n;
  const absolute = isNegative ? -tenthsOfPercent : tenthsOfPercent;
  return `${isNegative ? '-' : ''}${(absolute / 10n).toString()}.${(absolute % 10n).toString()}%`;
}

export function formatSnapshotTime(generatedAtUtc: string): string {
  const value = new Date(generatedAtUtc);
  if (Number.isNaN(value.getTime())) return 'Snapshot time unavailable';
  return new Intl.DateTimeFormat(undefined, {
    year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit', timeZoneName: 'short',
  }).format(value);
}
