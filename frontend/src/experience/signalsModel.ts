export type PreferenceObjective = 'active_time' | 'liquid_gold_deadline';
export type Strategy = 'trading_post' | 'crafting';
export type CoverageState = 'complete' | 'partial' | 'stale' | 'unknown';
export type MoneyConfidence = 'simulated' | 'estimated' | 'verified' | 'unknown';

export type MoneyView = Readonly<{
  copper: string;
  label: string;
  confidence: MoneyConfidence;
}>;

export type SessionPreferences = Readonly<{
  objective: PreferenceObjective;
  timeLimitMinutes: number;
  capitalPercent: number;
  minimumProfitPerPlan: MoneyView;
  minimumProfitPerActiveMinute: MoneyView;
  activities: Readonly<Record<Strategy, boolean>>;
  untouchedReserve: MoneyView;
  downsideTolerance: MoneyView;
  lockHorizonHours: number;
}>;

export type AccountCoverage = Readonly<{
  state: CoverageState;
  reviewedCharacters: number | null;
  totalCharacters: number | null;
  equipmentProtection: 'represented' | 'incomplete' | 'unknown';
  summary: string;
  explanation: string;
}>;

export type AnalysisStatus = Readonly<{
  state: 'simulated' | 'stale' | 'unavailable';
  label: string;
  detail: string;
}>;

export type SignalCard = Readonly<{
  id: string;
  strategy: Strategy;
  categoryLabel: string;
  title: string;
  itemLabel: string;
  netProfit: MoneyView | null;
  cashReleased: MoneyView | null;
  activeMinutes: number;
  capitalCommitted: MoneyView;
  note: Readonly<{ tone: 'neutral' | 'caution'; text: string }> | null;
  certainty: 'simulated';
  eligibility: 'actionable' | 'review_required';
  reasonCode: string | null;
  actionLabel: string;
}>;

export type AllocationSummary =
  | Readonly<{
      state: 'available';
      capitalBase: MoneyView;
      ceiling: MoneyView;
      committed: MoneyView;
      remaining: MoneyView;
    }>
  | Readonly<{ state: 'unavailable'; message: string }>;

export type SignalSessionSnapshot = Readonly<{
  scenario: 'normal' | 'urgent' | 'empty' | 'degraded' | 'deadline';
  signals: readonly SignalCard[];
  accountCoverage: AccountCoverage;
  analysis: AnalysisStatus;
  allocation: AllocationSummary;
  urgentAction: Readonly<{ title: string; description: string; capital: MoneyView; activeMinutes: number; actionLabel: string }> | null;
  emptyExplanation: string | null;
  objectiveExplanation: string;
}>;

export type PreviewScenario = 'normal' | 'urgent' | 'degraded';

export interface SignalsPreviewProvider {
  getDefaultPreferences(): SessionPreferences;
  saveDefaultPreferences(preferences: SessionPreferences): void;
  getSession(preferences: SessionPreferences): SignalSessionSnapshot;
}

