import type { MoneyView, SessionPreferences } from './signalsModel';

export type PlanScenario = 'comparison' | 'no-plan' | 'insufficient-capital' | 'stale' | 'active' | 'provisional' | 'wait' | 'partial' | 'contradiction' | 'long' | 'lost-ack' | 'conflict';
export type PreviewPhase = Readonly<{ id: string; title: string; description: string }>;
export type PreviewStep = Readonly<{
  id: string; phaseId: string; title: string; item: string; action: string; quantity: string;
  unitPrice: string | null; totalPrice: string | null; fees: string | null;
  actor: string; location: string; consumes: string; nextAction: string;
}>;
export type PreviewPlan = Readonly<{
  id: string; title: string; description: string; strategy: 'crafting' | 'trading_post';
  recommended: boolean; profit: MoneyView | null; cashReleased: MoneyView | null;
  capital: MoneyView; activeMinutes: number; waiting: string; cashCertainty: string;
  deadlineQualified: boolean; reason: string; phases: readonly PreviewPhase[]; steps: readonly PreviewStep[];
}>;
export type ComparisonSnapshot = Readonly<{
  plans: readonly PreviewPlan[];
  unavailable: Readonly<{ code: 'preferences' | 'capital' | 'stale'; explanation: string }> | null;
}>;
export type ExecutionState = 'ready' | 'pending' | 'confirmed' | 'safe-continuation' | 'required-wait' | 'partial' | 'contradiction' | 'undone' | 'paused' | 'complete';
export type PreviewExecution = Readonly<{
  id: string; accountScope: string; revision: string; plan: PreviewPlan; currentIndex: number;
  completedIds: readonly string[]; reportedIds: readonly string[]; reportedStepId: string | null; state: ExecutionState;
  message: string; waitEstimate: string | null; coverageComplete: boolean;
  remainingCapital: string; realizedProfit: string; reportedQuantity: string | null;
  eligibility: Readonly<{ report: boolean; undo: boolean; pause: boolean; resume: boolean; recheck: boolean; help: boolean; reason: string | null }>;
}>;
export type CompletionIntent = Readonly<{
  commandId: string; executionId: string; stepId: string; expectedRevision: string; accountScope: string;
}>;
export type StartResult = Readonly<{ kind: 'started'; execution: PreviewExecution }> | Readonly<{ kind: 'unavailable' | 'conflict'; explanation: string }>;
export type CompletionResult = Readonly<{ kind: 'acknowledged'; commandId: string }> | Readonly<{ kind: 'rejected'; explanation: string }>;
export type GuidanceAction = 'pause' | 'resume' | 'undo' | 'help' | 'recheck';
export interface PlansPreviewProvider {
  getComparison(preferences: SessionPreferences): ComparisonSnapshot;
  getAccountScope(): Promise<string>;
  getExecution(): PreviewExecution | null;
  startPlan(planId: string, preferences: SessionPreferences): Promise<StartResult>;
  reportPerformed(intent: CompletionIntent): Promise<CompletionResult>;
  refreshExecution(): Promise<void>;
  updateGuidance(action: GuidanceAction): Promise<void>;
  switchFixtureAccount(): void;
}
