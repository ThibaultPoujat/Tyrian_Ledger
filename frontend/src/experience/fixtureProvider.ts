import type {
  AccountCoverage,
  AllocationSummary,
  AnalysisStatus,
  MoneyView,
  PreviewScenario,
  SessionPreferences,
  SignalCard,
  SignalSessionSnapshot,
  SignalsPreviewProvider,
  Strategy,
} from './signalsModel';

const simulatedMoney = (copper: string, label: string): MoneyView => ({
  copper,
  label,
  confidence: 'simulated',
});

const DEFAULT_PREFERENCES: SessionPreferences = {
  objective: 'active_time',
  timeLimitMinutes: 15,
  capitalPercent: 30,
  minimumProfitPerPlan: simulatedMoney('20000', '2 po'),
  minimumProfitPerActiveMinute: simulatedMoney('5000', '0,5 po'),
  activities: { trading_post: true, crafting: true },
  untouchedReserve: simulatedMoney('300000', '30 po'),
  downsideTolerance: simulatedMoney('50000', '5 po'),
  lockHorizonHours: 24,
};

const NORMAL_COVERAGE: AccountCoverage = {
  state: 'complete',
  reviewedCharacters: 5,
  totalCharacters: 5,
  equipmentProtection: 'represented',
  summary: 'Couverture complète · simulation',
  explanation: 'Ces personnages et protections sont fictifs. Aucune couverture réelle du compte n’est confirmée.',
};

const NORMAL_ANALYSIS: AnalysisStatus = {
  state: 'simulated',
  label: 'Analyse de démonstration',
  detail: 'Mise à jour illustrative · il y a 1 min',
};

const STALE_COVERAGE: AccountCoverage = {
  state: 'partial',
  reviewedCharacters: 2,
  totalCharacters: 5,
  equipmentProtection: 'incomplete',
  summary: 'Couverture partielle · 2 personnages sur 5',
  explanation: 'Des personnages et l’état de l’équipement ne sont pas couverts. Les quantités inconnues ne sont pas considérées comme disponibles.',
};

const STALE_ANALYSIS: AnalysisStatus = {
  state: 'stale',
  label: 'Analyse partielle',
  detail: 'Compte fictif incomplet · données anciennes',
};

function signal(
  id: string,
  strategy: Strategy,
  categoryLabel: string,
  title: string,
  itemLabel: string,
  netProfit: MoneyView | null,
  cashReleased: MoneyView | null,
  activeMinutes: number,
  capitalCommitted: MoneyView,
  note: SignalCard['note'],
  actionLabel: string,
): SignalCard {
  return {
    id, strategy, categoryLabel, title, itemLabel, netProfit, cashReleased, activeMinutes, capitalCommitted, note,
    certainty: 'simulated',
    eligibility: note?.tone === 'caution' ? 'review_required' : 'actionable',
    reasonCode: note?.tone === 'caution' ? 'capital_lock_uncertain' : note === null ? null : 'illustrative_fixture',
    actionLabel,
  };
}

const CRAFT_SIGNAL = signal(
  'craft-short-batch',
  'crafting',
  'Artisanat · lot court',
  'Fabriquer un lot court',
  'Potion d’exploration supérieure',
  simulatedMoney('50000', '4–6 po estimés'),
  null,
  7,
  simulatedMoney('120000', '12 po'),
  { tone: 'neutral', text: 'Matières couvertes dans la simulation' },
  'Voir le plan',
);

const SURPLUS_SIGNAL = signal(
  'sell-surplus',
  'trading_post',
  'Comptoir · vente de surplus',
  'Vendre votre surplus',
  'Lingot d’orichalque',
  null,
  simulatedMoney('180000', '~18 po'),
  3,
  simulatedMoney('0', '0 po'),
  { tone: 'neutral', text: 'Or récupérable · profit non calculé' },
  'Voir le détail',
);

const FLIP_SIGNAL = signal(
  'buy-and-resell',
  'trading_post',
  'Comptoir · achat / revente',
  'Acheter et revendre',
  'Tissu de soie',
  simulatedMoney('40000', '3–5 po estimés'),
  null,
  4,
  simulatedMoney('200000', '20 po'),
  { tone: 'caution', text: 'Or immobilisé · durée incertaine' },
  'Voir le plan',
);

const SIGNALS = [CRAFT_SIGNAL, SURPLUS_SIGNAL, FLIP_SIGNAL] as const;

function allocationFor(percent: number): AllocationSummary {
  const fixed = {
    capitalBase: simulatedMoney('3000000', '300 po'),
    15: { ceiling: simulatedMoney('450000', '45 po'), remaining: simulatedMoney('200000', '20 po') },
    30: { ceiling: simulatedMoney('900000', '90 po'), remaining: simulatedMoney('650000', '65 po') },
    50: { ceiling: simulatedMoney('1500000', '150 po'), remaining: simulatedMoney('1250000', '125 po') },
  } as const;

  if (percent === 15 || percent === 30 || percent === 50) {
    const selected = fixed[percent];
    return {
      state: 'available',
      capitalBase: fixed.capitalBase,
      ceiling: selected.ceiling,
      committed: simulatedMoney('250000', '25 po'),
      remaining: selected.remaining,
    };
  }

  return {
    state: 'unavailable',
    message: 'Aucun plafond fictif n’est préparé pour cette valeur.',
  };
}

function hasAnyActivity(preferences: SessionPreferences): boolean {
  return preferences.activities.trading_post || preferences.activities.crafting;
}

function passesFixtureThreshold(preferences: SessionPreferences): boolean {
  return BigInt(preferences.minimumProfitPerPlan.copper) <= 20000n
    && BigInt(preferences.minimumProfitPerActiveMinute.copper) <= 5000n;
}

function filterActivities(signals: readonly SignalCard[], preferences: SessionPreferences): SignalCard[] {
  return signals.filter((candidate) => preferences.activities[candidate.strategy]);
}

function coverageFor(scenario: 'normal' | 'urgent' | 'empty' | 'degraded' | 'deadline'): AccountCoverage {
  return scenario === 'degraded' ? STALE_COVERAGE : NORMAL_COVERAGE;
}

function analysisFor(scenario: 'normal' | 'urgent' | 'empty' | 'degraded' | 'deadline'): AnalysisStatus {
  return scenario === 'degraded' ? STALE_ANALYSIS : NORMAL_ANALYSIS;
}

function buildSession(scenario: PreviewScenario, preferences: SessionPreferences): SignalSessionSnapshot {
  const allocation = allocationFor(preferences.capitalPercent);
  const accountCoverage = coverageFor(scenario);
  const analysis = analysisFor(scenario);

  if (scenario === 'degraded') {
    return {
      scenario: 'degraded',
      signals: [],
      accountCoverage,
      analysis,
      allocation,
      urgentAction: null,
      emptyExplanation: 'Aucun signal ne peut être confirmé tant que la couverture du compte reste partielle.',
      objectiveExplanation: 'Les ressources inconnues restent indisponibles dans cette démonstration.',
    };
  }

  if (scenario === 'urgent') {
    const signals = filterActivities(SIGNALS, preferences);
    return {
      scenario: 'urgent',
      signals,
      accountCoverage,
      analysis,
      allocation,
      urgentAction: {
        title: 'Une offre engagée dépasse votre limite',
        description: 'Vérifiez son maintien avant d’engager davantage d’or.',
        capital: simulatedMoney('180000', '18 po'),
        activeMinutes: 1,
        actionLabel: 'Examiner l’offre',
      },
      emptyExplanation: signals.length === 0 ? 'Aucune activité sélectionnée dans cette démonstration.' : null,
      objectiveExplanation: 'Résultats de session illustratifs fournis par un scénario figé.',
    };
  }

  if (preferences.objective === 'liquid_gold_deadline') {
    const deadlineSignals = preferences.activities.trading_post ? [SURPLUS_SIGNAL] : [];
    return {
      scenario: deadlineSignals.length > 0 ? 'deadline' : 'empty',
      signals: deadlineSignals,
      accountCoverage,
      analysis,
      allocation,
      urgentAction: null,
      emptyExplanation: deadlineSignals.length === 0 ? 'Le Comptoir n’est pas sélectionné pour cet objectif.' : null,
      objectiveExplanation: 'Seul l’or récupérable déjà détenu est présenté. Une vente future ou à délai incertain n’est pas comptée comme disponible à l’échéance.',
    };
  }

  const signals = filterActivities(SIGNALS, preferences);
  const meetsFixtureThreshold = preferences.timeLimitMinutes > 5 && passesFixtureThreshold(preferences);
  const selected = meetsFixtureThreshold ? signals : [];

  return {
    scenario: selected.length === 0 ? 'empty' : 'normal',
    signals: selected,
    accountCoverage,
    analysis,
    allocation,
    urgentAction: null,
    emptyExplanation: !hasAnyActivity(preferences)
      ? 'Sélectionnez une activité prise en charge pour afficher les résultats fictifs.'
      : !meetsFixtureThreshold
        ? 'Aucun signal fictif ne répond à ces seuils de session.'
        : 'Aucun signal ne mérite votre attention pour le moment.',
    objectiveExplanation: 'Durée de travail estimée par le fournisseur fictif. Le délai de vente reste indiqué séparément.',
  };
}

export function createFixtureProvider(scenario: PreviewScenario): SignalsPreviewProvider {
  let defaults = DEFAULT_PREFERENCES;

  return {
    getDefaultPreferences: () => defaults,
    saveDefaultPreferences: (preferences) => {
      defaults = preferences;
    },
    getSession: (preferences) => buildSession(scenario, preferences),
  };
}

export function clonePreferences(preferences: SessionPreferences): SessionPreferences {
  return {
    ...preferences,
    minimumProfitPerPlan: { ...preferences.minimumProfitPerPlan },
    minimumProfitPerActiveMinute: { ...preferences.minimumProfitPerActiveMinute },
    activities: { ...preferences.activities },
    untouchedReserve: { ...preferences.untouchedReserve },
    downsideTolerance: { ...preferences.downsideTolerance },
  };
}

export function scenarioFromSearch(search: string): PreviewScenario {
  const value = new URLSearchParams(search).get('scenario');
  return value === 'urgent' || value === 'degraded' ? value : 'normal';
}

export function moneyInputFromCopper(copper: string): string {
  const value = BigInt(copper);
  const whole = value / 10000n;
  const fraction = (value % 10000n) / 100n;
  if (fraction === 0n) return whole.toString();
  return whole.toString() + '.' + fraction.toString().padStart(2, '0').replace(/0$/, '');
}

export function copperFromMoneyInput(input: string): string | null {
  const normalized = input.trim().replace(',', '.');
  if (!/^(?:0|[1-9]\d*)(?:\.\d{1,2})?$/.test(normalized)) return null;
  const [whole, fraction = ''] = normalized.split('.');
  const fractionalCopper = (fraction + '00').slice(0, 2);
  try {
    return (BigInt(whole) * 10000n + BigInt(fractionalCopper) * 100n).toString();
  } catch {
    return null;
  }
}

