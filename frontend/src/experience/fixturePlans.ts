import type { PreviewScenario } from './signalsModel';
import type { CompletionIntent, CompletionResult, ExecutionState, GuidanceAction, PlansPreviewProvider, PreviewExecution, PreviewPhase, PreviewPlan, PreviewStep } from './planModel';

const money = (copper: string, label: string) => ({ copper, label, confidence: 'simulated' as const });
const phases: readonly PreviewPhase[] = [
  { id: 'acquire', title: 'Acquérir', description: 'Récupérez ou achetez les matériaux nécessaires.' },
  { id: 'craft', title: 'Fabriquer', description: 'Utilisez l’atelier pour préparer le lot fictif.' },
  { id: 'sell', title: 'Vendre', description: 'Mettez le lot en vente au Comptoir.' },
];
function step(id: string, phaseId: string, title: string, overrides: Partial<PreviewStep> = {}): PreviewStep {
  return { id, phaseId, title, item: 'Lingot de mithril', action: 'FABRIQUER', quantity: '50 lingots',
    unitPrice: null, totalPrice: null, fees: null, actor: 'Maëlys · personnage fictif', location: 'Atelier de fabrication',
    consumes: '100 minerais de mithril · réservés à ce plan fictif', nextAction: 'Fabriquer le composant', ...overrides };
}
const craftSteps = [
  step('craft-1', 'acquire', 'Récupérer les minerais', { action: 'RÉCUPÉRER', item: 'Minerai de mithril', quantity: '100 minerais', location: 'Banque de matériaux', consumes: 'Stock couvert dans la simulation' }),
  step('craft-2', 'acquire', 'Acheter les compléments', { action: 'ACHETER MAINTENANT', item: 'Complément fictif', quantity: '10 unités', unitPrice: '1 po', totalPrice: '10 po', consumes: '10 po de capital fictif', location: 'Comptoir' }),
  step('craft-3', 'craft', 'Préparer les lingots'),
  step('craft-4', 'craft', 'Fabriquer le composant', { item: 'Composant fictif', quantity: '10 composants', consumes: '50 lingots réservés · simulation', nextAction: 'Assembler le lot' }),
  step('craft-5', 'craft', 'Assembler le lot', { item: 'Lot d’artisanat fictif', quantity: '1 lot', consumes: '10 composants fictifs', nextAction: 'Mettre le lot en vente' }),
  step('craft-6', 'sell', 'Mettre le lot en vente', { action: 'METTRE EN VENTE', item: 'Lot d’artisanat fictif', quantity: '1 lot', unitPrice: '18 po', totalPrice: '18 po bruts', fees: '0,9 po de dépôt · modèle fictif', consumes: '1 lot et frais de dépôt fictifs', location: 'Comptoir', nextAction: 'Vérifier l’offre' }),
  step('craft-7', 'sell', 'Vérifier l’offre', { action: 'VÉRIFIER', item: 'Offre du lot fictif', quantity: '1 offre', consumes: 'Aucune ressource supplémentaire', location: 'Comptoir', nextAction: 'Attendre une vente · délai incertain' }),
];
const craft: PreviewPlan = {
  id: 'craft-short-batch', title: 'Artisanat ciblé', description: 'Préparez un lot à partir des matériaux de cette simulation.', strategy: 'crafting', recommended: true,
  profit: money('50000', '4–6 po'), cashReleased: null, capital: money('120000', '12 po'), activeMinutes: 7,
  waiting: 'Vente ultérieure · durée incertaine', cashCertainty: 'Après vente · non disponible à l’échéance', deadlineQualified: false,
  reason: 'Ce plan fictif laisse une marge dans vos 15 minutes et mobilise peu d’or.', phases, steps: craftSteps,
};
const surplus: PreviewPlan = {
  id: 'sell-surplus', title: 'Ventes immédiates', description: 'Vendez un surplus détenu dans cette simulation.', strategy: 'trading_post', recommended: false,
  profit: null, cashReleased: money('180000', '~18 po'), capital: money('0', '0 po'), activeMinutes: 3,
  waiting: 'Collecte incluse · ~3 min', cashCertainty: 'Vente immédiate et collecte · estimation, sans garantie', deadlineQualified: true,
  reason: 'Libère de l’or sans nouveau capital. Le coût d’origine est inconnu : aucun profit calculé.',
  phases: [{ id: 'sell', title: 'Vendre et collecter', description: 'Vérifiez l’offre immédiate puis récupérez l’or.' }],
  steps: [
    step('surplus-1', 'sell', 'Retrouver le surplus', { action: 'RÉCUPÉRER', item: 'Lingot d’orichalque', quantity: '20 lingots', consumes: 'Stock fictif disponible', location: 'Banque', nextAction: 'Vendre immédiatement' }),
    step('surplus-2', 'sell', 'Vendre immédiatement', { action: 'VENDRE MAINTENANT', item: 'Lingot d’orichalque', quantity: '20 lingots', unitPrice: '1 po', totalPrice: '20 po bruts', fees: '2 po · simulation', consumes: '20 lingots détenus', location: 'Comptoir', nextAction: 'Collecter l’or' }),
    step('surplus-3', 'sell', 'Collecter l’or', { action: 'COLLECTER', item: 'Or issu du surplus', quantity: '~18 po', consumes: 'Aucune ressource supplémentaire', location: 'Comptoir', nextAction: 'Exécution terminée · profit inconnu' }),
  ],
};
const flip: PreviewPlan = {
  ...craft, id: 'buy-and-resell', title: 'Achat / revente', description: 'Achetez puis mettez en vente un lot fictif.', strategy: 'trading_post', recommended: false,
  profit: money('40000', '3–5 po'), capital: money('200000', '20 po'), activeMinutes: 4,
  waiting: 'Or immobilisé · durée incertaine', reason: 'Gain modélisé uniquement; la vente future ne qualifie pas une échéance de liquidité.',
  phases: [phases[0], phases[2]], steps: [
    step('flip-1', 'acquire', 'Acheter le tissu', { action: 'ACHETER MAINTENANT', item: 'Tissu de soie', quantity: '100 pièces', unitPrice: '20 pa', totalPrice: '20 po', consumes: '20 po de capital fictif', location: 'Comptoir', nextAction: 'Vérifier l’achat' }),
    step('flip-2', 'acquire', 'Vérifier l’achat', { action: 'VÉRIFIER', item: 'Tissu de soie', quantity: '100 pièces', consumes: 'Aucune ressource supplémentaire', location: 'Comptoir', nextAction: 'Mettre le tissu en vente' }),
    step('flip-3', 'sell', 'Mettre le tissu en vente', { action: 'METTRE EN VENTE', item: 'Tissu de soie', quantity: '100 pièces', unitPrice: '28 pa', totalPrice: '28 po bruts', fees: '1,4 po de dépôt · simulation', consumes: '100 pièces et frais fictifs', location: 'Comptoir', nextAction: 'Attendre la vente' }),
    step('flip-4', 'sell', 'Suivre l’offre', { action: 'VÉRIFIER', item: 'Offre de tissu de soie', quantity: '1 offre', consumes: 'Aucune ressource supplémentaire', location: 'Comptoir', nextAction: 'Attendre · délai incertain' }),
  ],
};
const long: PreviewPlan = {
  ...craft, id: 'long', title: 'Artisanat · parcours détaillé', activeMinutes: 30,
  phases: Array.from({ length: 4 }, (_, index) => ({ id: 'batch-' + index, title: 'Lot ' + (index + 1), description: 'Six instructions fictives regroupées.' })),
  steps: Array.from({ length: 24 }, (_, index) => step('long-' + index, 'batch-' + Math.floor(index / 6), 'Préparer le lot · étape ' + (index + 1))),
};

const stateCopy: Record<ExecutionState, string> = {
  ready: 'Observation API simulée en cours. Effectuez uniquement l’instruction affichée.',
  pending: 'Déclaré effectué · vérification en attente',
  confirmed: 'Étape confirmée par l’observation API simulée. Voici l’instruction suivante.',
  'safe-continuation': 'Déclaré effectué · vérification en attente. La prochaine instruction est sûre dans cette simulation; aucun délai fixe n’est imposé.',
  'required-wait': 'La vente suivante exige la quantité d’achat confirmée. N’agissez pas avant une preuve complète et pertinente.',
  partial: '20 lingots sur 50 observés. Le reliquat de 30 reste incertain : instruction à recontrôler, aucune poursuite autorisée.',
  contradiction: 'L’observation simulée contredit la déclaration. Le travail dépendant est suspendu; ne suivez plus cette instruction.',
  undone: 'Déclaration locale annulée. Cela n’annule aucune action dans le jeu.',
  paused: 'Guidage en pause. Aucun ordre en jeu n’a été annulé.',
  complete: 'Instructions terminées. La vente et le résultat économique restent à observer.',
};
function withState(execution: PreviewExecution, state: ExecutionState, changes: Partial<PreviewExecution> = {}): PreviewExecution {
  const blocked = ['pending', 'required-wait', 'partial', 'contradiction', 'paused', 'complete'].includes(state);
  return { ...execution, ...changes, state, message: stateCopy[state], revision: String(Number(execution.revision) + 1),
    waitEstimate: state === 'required-wait' ? 'Prochaine lecture pertinente : environ 5 min, sans garantie de fraîcheur.' : null,
    eligibility: {
      report: !blocked, undo: execution.reportedStepId !== null && ['pending', 'safe-continuation', 'required-wait'].includes(state),
      pause: !['paused', 'contradiction', 'partial', 'complete'].includes(state), resume: state === 'paused',
      recheck: state === 'partial' || state === 'contradiction', help: state !== 'complete',
      reason: blocked ? 'Le fournisseur fictif interdit l’exécution pendant cette vérification ou pause.' : null,
    },
  };
}

/** Canned preview transitions only: no balances, external evidence matcher or production command engine. */
export function createPlansFixture(scenario: PreviewScenario): PlansPreviewProvider {
  let scope = 'fixture-account-1';
  let execution: PreviewExecution | null = null;
  let sequence = 0;
  let lostOnce = false;
  let undoTarget: PreviewExecution | null = null;
  let resumeTarget: PreviewExecution | null = null;
  const receipts = new Map<string, CompletionIntent>();
  const initialExecution = (plan: PreviewPlan, fromRoute: boolean, percent = 30): PreviewExecution => {
    const index = fromRoute ? plan.id === 'long' ? 13 : 2 : 0;
    const initial: PreviewExecution = {
      id: 'fixture-execution-' + (++sequence), accountScope: scope, revision: '1', plan, currentIndex: index,
      completedIds: plan.steps.slice(0, index).map(item => item.id), reportedIds: [], reportedStepId: null, state: 'ready', message: stateCopy.ready,
      waitEstimate: null, coverageComplete: scenario !== 'partial' && scenario !== 'contradiction',
      remainingCapital: (plan.id === 'sell-surplus' ? { 15: '20 po', 30: '65 po', 50: '125 po' } : plan.id === 'buy-and-resell' ? { 15: '0 po', 30: '45 po', 50: '105 po' } : { 15: '8 po', 30: '53 po', 50: '113 po' })[percent as 15 | 30 | 50] + ' · simulation', realizedProfit: 'Non établi', reportedQuantity: null,
      eligibility: { report: true, undo: false, pause: true, resume: false, recheck: false, help: true, reason: null },
    };
    if (['provisional', 'wait', 'partial', 'contradiction'].includes(scenario)) {
      undoTarget = initial;
      const reported = { ...initial, reportedStepId: plan.steps[index].id, reportedIds: [plan.steps[index].id] };
      return withState(reported, scenario === 'provisional' ? 'safe-continuation' : scenario === 'wait' ? 'required-wait' : scenario as 'partial' | 'contradiction',
        scenario === 'provisional' ? { currentIndex: index + 1 } : scenario === 'partial' ? { reportedQuantity: '20 / 50 · reliquat 30' } : {});
    }
    return initial;
  };
  if (['active', 'provisional', 'wait', 'partial', 'contradiction', 'long', 'lost-ack'].includes(scenario)) execution = initialExecution(scenario === 'long' ? long : craft, true);

  const getComparison: PlansPreviewProvider['getComparison'] = preferences => {
    const unavailable = (code: 'preferences' | 'capital' | 'stale', explanation: string) => ({ plans: [], unavailable: { code, explanation } });
    if (scenario === 'stale' || scenario === 'degraded') return unavailable('stale', 'Les preuves fictives sont anciennes ou incomplètes. Actualisez leur couverture avant de lancer un plan.');
    if (scenario === 'insufficient-capital' || ![15, 30, 50].includes(preferences.capitalPercent)) return unavailable('capital', 'Le capital disponible ne couvre aucun plan préparé pour cette allocation. Les engagements existants restent protégés.');
    if (scenario === 'no-plan' || preferences.timeLimitMinutes <= 5 || BigInt(preferences.minimumProfitPerPlan.copper) > 20000n || BigInt(preferences.minimumProfitPerActiveMinute.copper) > 5000n)
      return unavailable('preferences', 'Aucun plan fictif ne répond à cette durée et à ces seuils. Vous pouvez modifier vos préférences, sans relaxation automatique.');
    const plans = [craft, surplus, flip].filter(plan => preferences.activities[plan.strategy] && (preferences.objective !== 'liquid_gold_deadline' || plan.deadlineQualified));
    return plans.length ? { plans, unavailable: null } : unavailable('preferences', 'Aucun parcours avec or liquide ne correspond aux activités sélectionnées. Une vente future incertaine reste exclue.');
  };
  return {
    getComparison, getAccountScope: async () => scope, getExecution: () => execution,
    startPlan: async (id, preferences) => {
      if (execution !== null || scenario === 'conflict') return { kind: 'conflict', explanation: 'Un engagement fictif empêche ce démarrage. Aucune nouvelle exécution n’a été créée.' };
      const plan = getComparison(preferences).plans.find(candidate => candidate.id === id);
      if (plan === undefined) return { kind: 'unavailable', explanation: 'Ce plan n’est plus admissible pour cette session fictive. Modifiez vos préférences.' };
      execution = initialExecution(plan, false, preferences.capitalPercent);
      return { kind: 'started', execution };
    },
    reportPerformed: async intent => {
      const reject = (explanation: string): CompletionResult => ({ kind: 'rejected', explanation });
      if (intent.accountScope !== scope) return reject('Le compte fictif a changé. L’ancienne déclaration est invalidée.');
      const receipt = receipts.get(intent.commandId);
      if (receipt !== undefined) return JSON.stringify(receipt) === JSON.stringify(intent)
        ? { kind: 'acknowledged', commandId: intent.commandId } : reject('Cette identité de déclaration désigne une autre instruction.');
      if (execution === null || execution.id !== intent.executionId || execution.revision !== intent.expectedRevision
        || execution.plan.steps[execution.currentIndex]?.id !== intent.stepId || !execution.eligibility.report)
        return reject('Cette instruction est obsolète ou interdite. Actualisez le plan avant d’agir.');
      undoTarget = execution;
      const reported = { ...execution, reportedStepId: intent.stepId, reportedIds: [...execution.reportedIds, intent.stepId] };
      const nextIndex = execution.currentIndex + 1;
      execution = withState(reported, nextIndex < execution.plan.steps.length ? 'safe-continuation' : 'pending',
        nextIndex < execution.plan.steps.length ? { currentIndex: nextIndex } : {});
      receipts.set(intent.commandId, intent);
      if (scenario === 'lost-ack' && !lostOnce) { lostOnce = true; throw new Error('Simulated acknowledgement lost'); }
      return { kind: 'acknowledged', commandId: intent.commandId };
    },
    refreshExecution: async () => {
      if (execution === null || ['paused', 'partial', 'contradiction', 'complete'].includes(execution.state)) return;
      const current = execution.plan.steps[execution.currentIndex];
      if (execution.state === 'required-wait' || execution.state === 'pending') {
        const next = execution.currentIndex + 1;
        execution = withState(execution, next < execution.plan.steps.length ? 'confirmed' : 'complete', { currentIndex: Math.min(next, execution.plan.steps.length - 1), completedIds: [...execution.completedIds, current.id], reportedIds: [], reportedStepId: null });
      } else if (execution.state === 'safe-continuation') {
        execution = withState(execution, 'confirmed', { completedIds: [...execution.completedIds, ...execution.reportedIds], reportedIds: [], reportedStepId: null });
      } else {
        const next = execution.currentIndex + 1;
        execution = withState(execution, next < execution.plan.steps.length ? 'confirmed' : 'complete', { currentIndex: Math.min(next, execution.plan.steps.length - 1), completedIds: [...execution.completedIds, current.id], reportedIds: [], reportedStepId: null });
      }
      undoTarget = null;
    },
    updateGuidance: async (action: GuidanceAction) => {
      if (execution === null || !execution.eligibility[action]) return;
      if (action === 'pause') { resumeTarget = execution; execution = withState(execution, 'paused'); }
      if (action === 'resume' && resumeTarget !== null) { execution = { ...resumeTarget, revision: String(Number(execution.revision) + 1) }; resumeTarget = null; }
      if (action === 'undo' && undoTarget !== null) { execution = withState({ ...undoTarget, revision: execution.revision }, 'undone'); undoTarget = null; }
      if (action === 'help') execution = { ...execution, message: 'Si l’action diffère, suspendez le guidage et attendez une preuve pertinente. Une annulation locale ne modifie jamais le jeu.' };
      if (action === 'recheck') execution = withState(execution, 'required-wait', { reportedStepId: null, reportedQuantity: null });
    },
    switchFixtureAccount: () => { scope = scope === 'fixture-account-1' ? 'fixture-account-2' : 'fixture-account-1'; execution = null; undoTarget = null; resumeTarget = null; receipts.clear(); },
  };
}
