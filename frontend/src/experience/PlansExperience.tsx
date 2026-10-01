import { useEffect, useRef, useState } from 'react';
import type { SessionPreferences, SignalsPreviewProvider } from './signalsModel';
import type { CompletionIntent, GuidanceAction, PreviewExecution, PreviewPlan } from './planModel';
import { Icon } from './ExperienceIcon';
import './PlansExperience.css';

export function PlansExperience({ provider, preferences, visible, planHint, onEdit }: {
  provider: SignalsPreviewProvider; preferences: SessionPreferences; visible: boolean; planHint?: string; onEdit: () => void;
}) {
  const [selectedId, setSelectedId] = useState(planHint ?? 'craft-short-batch');
  const [execution, setExecution] = useState(() => provider.getExecution());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const busyRef = useRef(false);
  const pending = useRef<CompletionIntent | null>(null);
  const generation = useRef(0);
  const comparison = provider.getComparison(preferences);
  const allocation = provider.getAllocation(preferences.capitalPercent);
  const selected = comparison.plans.find(plan => plan.id === selectedId) ?? comparison.plans.find(plan => plan.recommended) ?? comparison.plans[0];
  useEffect(() => { if (planHint) setSelectedId(planHint); }, [planHint]);

  const perform = async (operation: () => Promise<void>) => {
    if (busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    setError(null);
    const token = generation.current;
    try { await operation(); }
    catch { if (token === generation.current) setError('L’accusé de réception fictif est perdu. Actualiser vérifiera le compte puis réessaiera la déclaration d’origine.'); }
    finally { if (token === generation.current) { busyRef.current = false; setBusy(false); } }
  };
  const transmit = async (intent: CompletionIntent, token: number) => {
    const result = await provider.reportPerformed(intent);
    const scope = await provider.getAccountScope();
    if (token !== generation.current) return;
    if (scope !== intent.accountScope) {
      pending.current = null;
      setExecution(provider.getExecution());
      setError('Le compte fictif a changé. L’ancienne déclaration est invalidée.');
      return;
    }
    pending.current = null;
    if (result.kind === 'rejected') setError(result.explanation);
    // Acknowledgements are historical receipts, never instruction snapshots.
    setExecution(provider.getExecution());
  };
  const report = () => {
    if (!execution || pending.current || busyRef.current || !execution.eligibility.report) return;
    const step = execution.plan.steps[execution.currentIndex];
    const intent: CompletionIntent = {
      commandId: crypto.randomUUID(), executionId: execution.id, stepId: step.id,
      expectedRevision: execution.revision, accountScope: execution.accountScope,
    };
    pending.current = intent;
    const token = generation.current;
    void perform(() => transmit(intent, token));
  };
  const refresh = () => {
    const token = generation.current;
    void perform(async () => {
      const scope = await provider.getAccountScope();
      if (token !== generation.current) return;
      if (pending.current !== null) {
        const intent = pending.current;
        if (scope !== intent.accountScope) {
          pending.current = null;
          setError('Le compte fictif a changé. L’ancienne déclaration est invalidée.');
          setExecution(provider.getExecution());
          return;
        }
        await transmit(intent, token);
        return;
      }
      await provider.refreshExecution();
      if (token === generation.current) setExecution(provider.getExecution());
    });
  };
  const guidance = (action: GuidanceAction) => {
    const token = generation.current;
    void perform(async () => {
      await provider.updateGuidance(action);
      if (token === generation.current) setExecution(provider.getExecution());
    });
  };
  const start = () => {
    if (!selected) return;
    const token = generation.current;
    void perform(async () => {
      const result = await provider.startPlan(selected.id, preferences);
      if (token !== generation.current) return;
      if (result.kind !== 'started') setError(result.explanation);
      setExecution(provider.getExecution());
    });
  };
  const switchAccount = () => {
    generation.current++;
    pending.current = null;
    busyRef.current = false;
    setBusy(false);
    provider.switchFixtureAccount();
    setExecution(provider.getExecution());
    setError('Compte fictif changé. Les anciennes déclarations sont invalidées.');
  };

  // Keep command recovery alive across navigation without duplicating hidden screen content.
  if (!visible) return null;
  return <section className="p05b-experience" aria-label="Plans de démonstration">
    {execution === null ? <>
      <header className="p05b-header"><div><h1>{preferences.objective === 'active_time' ? `Que faire en ${preferences.timeLimitMinutes} minutes ?` : `Récupérer de l’or sous ${preferences.timeLimitMinutes} minutes`}</h1><p>Des alternatives pour votre session · aucune ressource réservée.</p></div><span className="p05a-demo-chip">FICTIF</span></header>
      <div className="p05b-comparison">
        <div className="p05b-alternatives">
          <div className="p05b-session-strip"><span><Icon name="clock" />{preferences.timeLimitMinutes} min {preferences.objective === 'active_time' ? 'pour agir' : 'avant liquidité'}</span><span><Icon name="coins" />Budget {preferences.capitalPercent} %</span><span>{[preferences.activities.trading_post ? 'Comptoir' : '', preferences.activities.crafting ? 'Artisanat' : ''].filter(Boolean).join(' + ') || 'Aucune activité'}</span><button className="p05a-secondary-button" onClick={onEdit} type="button">Modifier</button></div>
          {allocation.state === 'available' && <p className="p05b-budget"><Icon name="coins" /><strong>{allocation.remaining.label}</strong> encore mobilisables · {allocation.committed.label} déjà engagés · plafond {allocation.ceiling.label} · simulation</p>}
          {comparison.unavailable ? <div className="p05b-panel p05b-empty"><Icon name="info" /><h2>Aucun plan admissible</h2><p>{comparison.unavailable.explanation}</p><button className="p05a-primary-button" onClick={onEdit} type="button">Adapter ma session</button></div> : <>
            <div className="p05b-choice-list" role="group" aria-label="Choisir un plan alternatif">
              {comparison.plans.map(plan => <article key={plan.id} className={'p05b-plan-choice' + (selected?.id === plan.id ? ' p05b-plan-choice--selected' : '')}>
                <PlanArtwork plan={plan} />
                <div className="p05b-choice-content"><div className="p05b-choice-title"><h2>{plan.title}</h2>{plan.recommended && <span className="p05b-recommended">★ Conseillé pour cette session</span>}</div><p>{plan.description}</p><PlanMetrics plan={plan} /><p className="p05b-small">{plan.steps.length} étapes · 1 personnage · Encaissement : {plan.cashCertainty}</p>{plan.profit === null && <p className="p05b-small">Coût d’origine inconnu · profit non calculé</p>}</div>
                <button className="p05a-secondary-button p05b-select" aria-pressed={selected?.id === plan.id} aria-label={'Comparer ' + plan.title} onClick={() => { setSelectedId(plan.id); setError(null); }} type="button">Comparer</button>
              </article>)}
            </div>
            <p className="p05b-reason"><Icon name="info" /><strong>Pourquoi ce choix ?</strong> {selected?.reason}</p>
          </>}
          <p className="p05b-small">Les plans peuvent partager des ressources. Ce sont des alternatives, jamais des engagements simultanés.</p>
        </div>
        <aside className="p05b-panel p05b-recap" aria-label="Récapitulatif du plan sélectionné">
          {selected ? <><div className="p05b-recap-title"><PlanArtwork plan={selected} /><div><h2>{selected.title}</h2><p>{selected.description}</p></div></div><h3>Récapitulatif</h3><PlanMetrics plan={selected} vertical /><p>{selected.steps.length} étapes · 1 personnage</p><p className="p05b-small">{selected.cashCertainty}</p><h3>Détail des étapes</h3><PhasePreview plan={selected} /><button className="p05a-primary-button p05b-start" disabled={busy} onClick={start} type="button">{busy ? 'Vérification fictive…' : 'Démarrer ce plan'}</button><p className="p05b-small">Démarrage simulé uniquement. Les actions restent manuelles en jeu.</p></> : <><h2>Votre session</h2><p>Les préférences restent intactes. Aucune nouvelle réservation.</p></>}
          <p className="p05b-small">Une vente future incertaine ne devient jamais de l’or liquide à l’échéance.</p>
        </aside>
      </div>
    </> : <ActivePlan execution={execution} busy={busy || pending.current !== null} visible={visible} onReport={report} onRefresh={refresh} onGuidance={guidance} />}
    {error && <p className="p05b-error" role="alert">{error}<button className="p05a-secondary-button" disabled={busy} onClick={refresh} type="button">Actualiser</button></p>}
    <details className="p05b-fixture-tools"><summary>Compte de démonstration</summary><p>Changement de contexte en mémoire; aucune connexion réelle.</p><button className="p05a-secondary-button" onClick={switchAccount} type="button">Changer de compte fictif</button></details>
  </section>;
}

function PlanArtwork({ plan }: { plan: PreviewPlan }) {
  return <div className={'p05b-artwork p05b-artwork--' + (plan.strategy === 'crafting' ? 'craft' : plan.cashReleased ? 'cash' : 'trade')} aria-hidden="true"><Icon name={plan.strategy === 'crafting' ? 'hammer' : plan.cashReleased ? 'coins' : 'swap'} /><span>◆</span></div>;
}
function PlanMetrics({ plan, vertical = false }: { plan: PreviewPlan; vertical?: boolean }) {
  return <dl className={vertical ? 'p05b-metrics p05b-metrics--vertical' : 'p05b-metrics'}>
    <div><dt><Icon name="coins" />{plan.cashReleased ? 'Or récupérable' : 'Gain net modélisé'}</dt><dd className="p05b-gold">{plan.cashReleased?.label ?? plan.profit?.label ?? 'Inconnu'}</dd></div>
    <div><dt><Icon name="clock" />Temps actif estimé</dt><dd>~ {plan.activeMinutes} min</dd></div>
    <div><dt><Icon name="coins" />Or mobilisé</dt><dd>{plan.capital.label}</dd></div>
  </dl>;
}
function PhasePreview({ plan }: { plan: PreviewPlan }) {
  return <ol className="p05b-phase-preview">{plan.phases.map((phase, index) => <li key={phase.id}><span aria-hidden="true">{index + 1}</span><details open><summary>{phase.title} · {plan.steps.filter(step => step.phaseId === phase.id).length} étapes</summary><p>{phase.description}</p></details></li>)}</ol>;
}
function ActivePlan({ execution, busy, visible, onReport, onRefresh, onGuidance }: {
  execution: PreviewExecution; busy: boolean; visible: boolean; onReport: () => void; onRefresh: () => void; onGuidance: (action: GuidanceAction) => void;
}) {
  const { plan } = execution;
  const current = plan.steps[execution.currentIndex];
  const heading = useRef<HTMLHeadingElement>(null);
  const currentPhaseStep = useRef<HTMLLIElement>(null);
  const [copyMessage, setCopyMessage] = useState<string | null>(null);
  const currentIdentity = execution.id + ':' + current.id;
  useEffect(() => {
    setCopyMessage(null);
    if (!visible) return;
    heading.current?.focus({ preventScroll: true });
    currentPhaseStep.current?.scrollIntoView?.({ block: 'nearest' });
  }, [currentIdentity, visible]);
  const copy = async () => {
    try { await navigator.clipboard.writeText(current.item); setCopyMessage('Nom copié.'); }
    catch { setCopyMessage('Copie indisponible. Sélectionnez le nom affiché.'); }
  };
  const next = plan.steps[execution.currentIndex + 1];
  return <>
    <header className="p05b-header p05b-header--active"><div><p className="p05b-small">Plans / {plan.title}</p><h1>{plan.title}</h1><p>Étape {execution.currentIndex + 1} sur {plan.steps.length} · temps actif total estimé : ~ {plan.activeMinutes} min</p></div><span className="p05a-demo-chip">FICTIF</span></header>
    <div className="p05b-execution">
      <aside className="p05b-panel p05b-phases" aria-label="Étapes du plan"><h2>Étapes du plan</h2><div className="p05b-phase-scroll">
        {plan.phases.map(phase => {
          const steps = plan.steps.filter(step => step.phaseId === phase.id);
          const count = steps.filter(step => execution.completedIds.includes(step.id)).length;
          return <details key={phase.id} open={phase.id === current.phaseId}><summary>{phase.title} · {count}/{steps.length}</summary><ol>{steps.map(step => {
            const index = plan.steps.indexOf(step);
            const isCurrent = step.id === current.id;
            return <li key={step.id} ref={isCurrent ? currentPhaseStep : undefined} aria-current={isCurrent ? 'step' : undefined} className={isCurrent ? 'p05b-current-phase-step' : ''}><span aria-hidden="true">{execution.completedIds.includes(step.id) ? '✓' : index + 1}</span><div>{step.title}<small>{execution.completedIds.includes(step.id) ? 'Confirmé · simulation' : execution.reportedIds.includes(step.id) ? 'Déclaré · en attente' : isCurrent ? 'Instruction actuelle' : 'À venir'}</small></div></li>;
          })}</ol></details>;
        })}
      </div></aside>
      <section className="p05b-panel p05b-instruction" aria-label="Instruction actuelle"><h2><Icon name="hammer" />À faire dans le jeu</h2><div className="p05b-instruction-card">
        <div className="p05b-item"><div className="p05b-item-art" aria-hidden="true">◇</div><h3 ref={heading} tabIndex={-1}>{current.item}</h3><button className="p05a-secondary-button" onClick={() => void copy()} type="button">Copier le nom</button></div>
        {copyMessage && <p role="status">{copyMessage}</p>}
        <p className="p05b-action-label">{current.action}</p><p className="p05b-quantity">{current.quantity}</p><p>Consomme {current.consumes}</p>
        {current.unitPrice && <dl className="p05b-price"><div><dt>Prix unitaire / limite</dt><dd>{current.unitPrice}</dd></div><div><dt>Total instruit</dt><dd>{current.totalPrice}</dd></div>{current.fees && <div><dt>Frais modélisés</dt><dd>{current.fees}</dd></div>}</dl>}
        <p className="p05b-location">Personnage : {current.actor}<br />Lieu : {current.location}</p>
        <p className={'p05b-coverage ' + (execution.coverageComplete ? 'p05b-coverage--complete' : '')}>{execution.coverageComplete ? '✓ Matériaux et protections couverts · simulation complète' : 'Couverture incomplète · protections non confirmées'}</p>
        <div className={'p05b-state p05b-state--' + execution.state} role="status"><strong>{execution.state === 'confirmed' ? 'Observation API simulée' : execution.state === 'safe-continuation' || execution.state === 'pending' ? 'Déclaré effectué · vérification en attente' : execution.state === 'ready' ? 'Vérification automatique simulée' : 'Vérification / récupération'}</strong><p>{execution.message}</p>{execution.waitEstimate && <p>{execution.waitEstimate}</p>}{execution.reportedQuantity && <p>Quantité observée : {execution.reportedQuantity}</p>}</div>
        <div className="p05b-instruction-actions"><button className="p05a-secondary-button" disabled={busy} onClick={onRefresh} type="button">Actualiser l’observation</button><button className="p05a-primary-button" aria-describedby="p05b-eligibility" disabled={busy || !execution.eligibility.report} onClick={onReport} type="button">J’ai effectué cette étape</button></div>
        <p className="p05b-small" id="p05b-eligibility">{execution.eligibility.reason ?? 'Action exceptionnelle : déclaration locale, jamais une confirmation API.'}</p>
      </div><div className="p05b-guidance-actions">{([
        ['help', 'Un problème avec cette étape ?'], ['pause', 'Mettre le guidage en pause'], ['resume', 'Reprendre le guidage'], ['undo', 'Annuler la déclaration locale'], ['recheck', 'Recontrôler l’instruction'],
      ] as const).map(([action, label]) => <button key={action} className="p05a-quiet-button" disabled={busy || !execution.eligibility[action]} onClick={() => onGuidance(action)} type="button">{label}</button>)}</div><p className="p05b-small">Pause et annulation locale ne modifient aucun ordre en jeu.</p></section>
      <aside className="p05b-execution-recap"><section className="p05b-panel"><h2><Icon name="chart" />Ce plan en bref</h2><PlanMetrics plan={plan} vertical /><dl className="p05b-detail-metrics"><div><dt>Encaissement</dt><dd>{plan.cashCertainty}</dd></div><div><dt>Attente de marché</dt><dd>{plan.waiting}</dd></div><div><dt>Capital de session restant</dt><dd>{execution.remainingCapital}</dd></div><div><dt>Résultat réalisé</dt><dd>{execution.realizedProfit}</dd></div></dl></section><section className="p05b-panel p05b-next"><h2>Prochaine étape</h2><p><strong>{next?.title ?? 'Observation du résultat'}</strong></p><p className="p05b-small">{execution.state === 'safe-continuation' ? 'Instruction actuelle autorisée sans délai fixe. La déclaration précédente reste provisoire.' : next ? 'Suivez uniquement l’instruction actuelle; le fournisseur déterminera la suite.' : 'Aucune vente ni aucun profit confirmé par cet écran.'}</p><p className="p05b-small">Suite prévue : {current.nextAction}</p></section></aside>
    </div>
  </>;
}
