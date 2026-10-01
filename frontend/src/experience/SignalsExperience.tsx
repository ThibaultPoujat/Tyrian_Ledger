import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { clonePreferences, copperFromMoneyInput, moneyInputFromCopper } from './fixtureProvider';
import type {
  AllocationSummary,
  PreferenceObjective,
  SessionPreferences,
  SignalCard,
  SignalSessionSnapshot,
  SignalsPreviewProvider,
  Strategy,
} from './signalsModel';
import './SignalsExperience.css';
import { PlansExperience } from './PlansExperience';
import { Icon } from './ExperienceIcon';

type Destination = 'signals' | 'plans' | 'results' | 'settings';

type SignalsExperienceProps = Readonly<{
  provider: SignalsPreviewProvider;
  initialDestination?: 'signals' | 'plans';
}>;

type SessionDraft = {
  objective: PreferenceObjective;
  timeLimitMinutes: string;
  capitalPercent: string;
  minimumProfitPerPlan: string;
  minimumProfitPerActiveMinute: string;
  activities: Record<Strategy, boolean>;
  untouchedReserve: string;
  downsideTolerance: string;
  lockHorizonHours: string;
};

function draftFromPreferences(preferences: SessionPreferences): SessionDraft {
  return {
    objective: preferences.objective,
    timeLimitMinutes: String(preferences.timeLimitMinutes),
    capitalPercent: String(preferences.capitalPercent),
    minimumProfitPerPlan: moneyInputFromCopper(preferences.minimumProfitPerPlan.copper),
    minimumProfitPerActiveMinute: moneyInputFromCopper(preferences.minimumProfitPerActiveMinute.copper),
    activities: { ...preferences.activities },
    untouchedReserve: moneyInputFromCopper(preferences.untouchedReserve.copper),
    downsideTolerance: moneyInputFromCopper(preferences.downsideTolerance.copper),
    lockHorizonHours: String(preferences.lockHorizonHours),
  };
}

function moneyFromInput(input: string, confidence: 'simulated' | 'estimated' = 'simulated') {
  const copper = copperFromMoneyInput(input);
  if (copper === null) return null;
  const normalized = input.trim().replace(',', '.');
  return {
    copper,
    label: normalized.replace('.', ',') + ' po',
    confidence,
  } as const;
}

function preferencesFromDraft(draft: SessionDraft): SessionPreferences | null {
  const timeLimitMinutes = Number(draft.timeLimitMinutes);
  const capitalPercent = Number(draft.capitalPercent);
  const lockHorizonHours = Number(draft.lockHorizonHours);
  const minimumProfitPerPlan = moneyFromInput(draft.minimumProfitPerPlan);
  const minimumProfitPerActiveMinute = moneyFromInput(draft.minimumProfitPerActiveMinute);
  const untouchedReserve = moneyFromInput(draft.untouchedReserve);
  const downsideTolerance = moneyFromInput(draft.downsideTolerance);

  if (!Number.isSafeInteger(timeLimitMinutes) || timeLimitMinutes <= 0) return null;
  if (!Number.isSafeInteger(capitalPercent) || capitalPercent < 0 || capitalPercent > 100) return null;
  if (!Number.isSafeInteger(lockHorizonHours) || lockHorizonHours < 0) return null;
  if (minimumProfitPerPlan === null || minimumProfitPerActiveMinute === null
    || untouchedReserve === null || downsideTolerance === null) return null;

  return {
    objective: draft.objective,
    timeLimitMinutes,
    capitalPercent,
    minimumProfitPerPlan,
    minimumProfitPerActiveMinute,
    activities: { ...draft.activities },
    untouchedReserve,
    downsideTolerance,
    lockHorizonHours,
  };
}

export function SignalsExperience({ provider, initialDestination = 'signals' }: SignalsExperienceProps) {
  const [preferences, setPreferences] = useState(() => clonePreferences(provider.getDefaultPreferences()));
  const [destination, setDestination] = useState<Destination>(initialDestination);
  const [planHint, setPlanHint] = useState<string | undefined>();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [drawerSeed, setDrawerSeed] = useState(() => clonePreferences(provider.getDefaultPreferences()));
  const [drawerInstance, setDrawerInstance] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);
  const session = provider.getSession(preferences);

  const openDrawer = useCallback((timeLimitMinutes?: number) => {
    const seed = clonePreferences(preferences);
    setDrawerSeed(timeLimitMinutes === undefined ? seed : { ...seed, timeLimitMinutes });
    setDrawerInstance((current) => current + 1);
    setNotice(null);
    setDrawerOpen(true);
  }, [preferences]);

  const closeDrawer = useCallback(() => setDrawerOpen(false), []);
  const applyPreferences = useCallback((next: SessionPreferences) => {
    setPreferences(clonePreferences(next));
    setNotice('Préférences appliquées à cette session de démonstration.');
    setDestination('signals');
  }, []);
  const saveDefault = useCallback((next: SessionPreferences) => {
    provider.saveDefaultPreferences(clonePreferences(next));
  }, [provider]);

  const showPlaceholder = (next: Destination) => {
    setDestination(next);
    setNotice(null);
  };

  return (
    <div className="p05a-app">
      <a className="p05a-skip-link" href="#p05a-main">Aller au contenu principal</a>
      <aside className="p05a-rail">
        <div className="p05a-brand" aria-label="Tyrian Ledger">
          <span className="p05a-brand-seal" aria-hidden="true">TL</span>
          <span className="p05a-brand-name">Tyrian Ledger</span>
          <span className="p05a-brand-subtitle">Vos profits restent en jeu</span>
          <span className="p05a-brand-divider" aria-hidden="true">◆</span>
        </div>
        <nav className="p05a-navigation" aria-label="Navigation principale">
          <NavigationButton active={destination === 'signals'} label="Signaux" icon="compass" onClick={() => showPlaceholder('signals')} />
          <NavigationButton active={destination === 'plans'} label="Plans" icon="plan" onClick={() => showPlaceholder('plans')} />
          <NavigationButton active={destination === 'results'} label="Bilan" icon="chart" onClick={() => showPlaceholder('results')} />
          <NavigationButton active={destination === 'settings'} label="Réglages" icon="settings" onClick={() => showPlaceholder('settings')} />
        </nav>
        <div className="p05a-demo-rail">
          <span aria-hidden="true" className="p05a-demo-dot" />
          <span>Démonstration · données fictives</span>
        </div>
      </aside>

      <div className="p05a-workspace" aria-hidden={drawerOpen}>
        <main id="p05a-main" className="p05a-main">
          {destination === 'signals' ? (
            <SignalsPage session={session} preferences={preferences} onEdit={openDrawer} onAction={(id) => { setPlanHint(id); showPlaceholder('plans'); }} />
          ) : destination !== 'plans' ? (
            <PreviewPlaceholder destination={destination} onBack={() => showPlaceholder('signals')} />
          ) : null}
          <PlansExperience provider={provider} preferences={preferences} visible={destination === 'plans'} planHint={planHint} onEdit={() => openDrawer()} />
        </main>
        <footer className="p05a-status-bar">
          <span className={session.analysis.state === 'stale' ? 'p05a-status-dot p05a-status-dot--warning' : 'p05a-status-dot'} aria-hidden="true" />
          <span>{session.analysis.label}</span>
          <span aria-hidden="true">·</span>
          <span>{session.analysis.detail}</span>
          {session.accountCoverage.state !== 'complete' && (
            <span className="p05a-status-coverage">{session.accountCoverage.summary}</span>
          )}
          <span className="p05a-status-demo">Démonstration · données fictives</span>
        </footer>
      </div>

      {notice !== null && !drawerOpen && <p className="p05a-live-notice" role="status" aria-live="polite">{notice}</p>}

      {drawerOpen && (
        <SessionPreferencesDrawer
          key={drawerInstance}
          initialPreferences={drawerSeed}
          getAllocation={provider.getAllocation}
          onApply={applyPreferences}
          onSaveDefault={saveDefault}
          onClose={closeDrawer}
        />
      )}
    </div>
  );
}


function NavigationButton({ active, label, icon, onClick }: { active: boolean; label: string; icon: 'compass' | 'plan' | 'chart' | 'settings'; onClick: () => void }) {
  return (
    <button
      className={active ? 'p05a-nav-item p05a-nav-item--active' : 'p05a-nav-item'}
      type="button"
      aria-current={active ? 'page' : undefined}
      onClick={onClick}
    >
      <Icon name={icon} />
      <span>{label}</span>
    </button>
  );
}

function SignalsPage({ session, preferences, onEdit, onAction }: {
  session: SignalSessionSnapshot;
  preferences: SessionPreferences;
  onEdit: (timeLimitMinutes?: number) => void;
  onAction: (id?: string) => void;
}) {
  const objectiveTitle = preferences.objective === 'active_time' ? 'Temps pour agir' : 'Or disponible avant…';
  const account = session.accountCoverage;
  return (
    <>
      <header className="p05a-page-header">
        <div>
          <p className="p05a-eyebrow">Assistant d’action</p>
          <h1>Signaux</h1>
          <p className="p05a-intro">Les opportunités adaptées à votre session</p>
        </div>
        <div className={account.state === 'partial' || account.state === 'stale' ? 'p05a-coverage p05a-coverage--warning' : 'p05a-coverage'}>
          <Icon name="plan" />
          <span>{account.summary}</span>
          <span className="p05a-demo-chip">Fictif</span>
        </div>
      </header>

      <section className="p05a-session-strip" aria-label="Résumé de session">
        <div className="p05a-session-group p05a-session-time">
          <span className="p05a-group-label">{objectiveTitle}</span>
          <div className="p05a-segmented-control" role="group" aria-label="Durée de session">
            {[5, 15, 30].map((minutes) => (
              <button
                key={minutes}
                className={preferences.timeLimitMinutes === minutes ? 'p05a-segment p05a-segment--selected' : 'p05a-segment'}
                aria-pressed={preferences.timeLimitMinutes === minutes}
                onClick={() => onEdit(minutes)}
                type="button"
              >
                {minutes} min
              </button>
            ))}
          </div>
        </div>
        <div className="p05a-session-group">
          <span className="p05a-group-label">Budget mobilisable</span>
          <button className="p05a-summary-button" onClick={() => onEdit()} type="button" aria-label={'Modifier le budget, ' + preferences.capitalPercent + ' pour cent'}>
            <Icon name="coins" /><strong>{preferences.capitalPercent} %</strong><span aria-hidden="true">⌄</span>
          </button>
        </div>
        <div className="p05a-session-group p05a-session-activities">
          <span className="p05a-group-label">Activités</span>
          <div className="p05a-chip-row">
            {preferences.activities.trading_post && <span className="p05a-outline-chip"><Icon name="swap" />Comptoir</span>}
            {preferences.activities.crafting && <span className="p05a-outline-chip"><Icon name="hammer" />Artisanat</span>}
            {!preferences.activities.trading_post && !preferences.activities.crafting && <span className="p05a-muted-chip">Aucune sélection</span>}
          </div>
        </div>
        <div className="p05a-session-group">
          <span className="p05a-group-label">Gain net minimum</span>
          <button className="p05a-summary-button" onClick={() => onEdit()} type="button" aria-label="Modifier les gains minimums">
            <Icon name="coins" /><strong>{preferences.minimumProfitPerPlan.label}</strong><span aria-hidden="true">⌄</span>
          </button>
        </div>
        <button className="p05a-primary-button p05a-adapt-button" onClick={() => onEdit()} type="button">
          <Icon name="settings" />Adapter ma session
        </button>
      </section>

      {session.urgentAction !== null && (
        <section className="p05a-urgent-band" aria-labelledby="p05a-urgent-title">
          <div className="p05a-urgent-copy">
            <span className="p05a-urgent-label"><Icon name="warning" />À vérifier maintenant</span>
            <h2 id="p05a-urgent-title">{session.urgentAction.title}</h2>
            <p>{session.urgentAction.description}</p>
          </div>
          <div className="p05a-urgent-metric">
            <span>Capital concerné · simulation</span>
            <strong>{session.urgentAction.capital.label}</strong>
          </div>
          <div className="p05a-urgent-metric">
            <span>Action estimée</span>
            <strong><Icon name="clock" />&lt; {session.urgentAction.activeMinutes} min</strong>
          </div>
          <button className="p05a-primary-button" onClick={() => onAction()} type="button">{session.urgentAction.actionLabel}<span aria-hidden="true">›</span></button>
        </section>
      )}

      {session.signals.length > 0 ? (
        <>
          <div className="p05a-signals-heading">
            <h2>{session.signals.length} signaux à considérer</h2>
            {session.scenario === 'deadline' && <p>Objectif : or récupérable avant l’échéance</p>}
          </div>
          <section className="p05a-card-grid" aria-label="Actions de démonstration">
            {session.signals.map((card) => <SignalCardView key={card.id} card={card} onAction={() => onAction(card.id)} />)}
          </section>
          <p className="p05a-compatibility-note"><Icon name="info" /> Aperçu fictif. La compatibilité des ressources est vérifiée par le moteur lors de l’intégration.</p>
          {session.scenario === 'deadline' && <p className="p05a-deadline-explanation">{session.objectiveExplanation}</p>}
        </>
      ) : (
        <section className="p05a-empty-state" aria-labelledby="p05a-empty-title">
          <span className="p05a-empty-icon" aria-hidden="true"><Icon name="compass" /></span>
          <p className="p05a-eyebrow">Rien à lancer</p>
          <h2 id="p05a-empty-title">Aucun signal ne mérite votre attention pour le moment.</h2>
          <p>{session.emptyExplanation}</p>
          <p className="p05a-empty-objective">{session.objectiveExplanation}</p>
          {account.state !== 'complete' && <p className="p05a-degraded-message" role="status"><Icon name="warning" />{account.explanation}</p>}
          <button className="p05a-secondary-button" onClick={() => onEdit()} type="button">Adapter ma session</button>
        </section>
      )}

      <section className="p05a-plan-summary" aria-label="Plans en cours de démonstration">
        <div className="p05a-plan-summary-card p05a-plan-summary-card--active">
          <span className="p05a-plan-state-icon" aria-hidden="true">▶</span>
          <div><strong>En cours</strong><span>1 plan · reprise possible · simulation</span></div>
          <button className="p05a-quiet-button" onClick={() => onAction()} type="button">Reprendre</button>
        </div>
        <div className="p05a-plan-summary-card">
          <span className="p05a-plan-state-icon p05a-plan-state-icon--waiting" aria-hidden="true">Ⅱ</span>
          <div><strong>En attente</strong><span>2 ventes · aucune action nécessaire</span></div>
          <button className="p05a-icon-button" onClick={() => onAction()} type="button" aria-label="Afficher l’aperçu des plans"><span aria-hidden="true">›</span></button>
        </div>
      </section>
    </>
  );
}

function SignalCardView({ card, onAction }: { card: SignalCard; onAction: () => void }) {
  return (
    <article className="p05a-signal-card">
      <div className="p05a-card-title-row">
        <span className="p05a-card-icon" aria-hidden="true"><Icon name={card.strategy === 'crafting' ? 'hammer' : card.cashReleased !== null ? 'coins' : 'swap'} /></span>
        <div>
          <p className="p05a-card-category">{card.categoryLabel}</p>
          <h3>{card.title}</h3>
          <p className="p05a-item-label">{card.itemLabel}</p>
        </div>
        <span className="p05a-card-chevron" aria-hidden="true">›</span>
      </div>
      <dl className="p05a-card-metrics">
        <div>
          <dt>{card.cashReleased !== null ? 'Or récupérable' : 'Gain net estimé'}</dt>
          <dd>{card.cashReleased?.label ?? card.netProfit?.label ?? 'À confirmer'}</dd>
        </div>
        <div>
          <dt>Temps actif</dt>
          <dd><Icon name="clock" />~ {card.activeMinutes} min</dd>
        </div>
        <div>
          <dt>{card.cashReleased !== null ? 'Nouveau capital' : 'Or mobilisé'}</dt>
          <dd><Icon name="coins" />{card.capitalCommitted.label}</dd>
        </div>
      </dl>
      {card.note !== null && <p className={card.note.tone === 'caution' ? 'p05a-card-note p05a-card-note--caution' : 'p05a-card-note'}>{card.note.tone === 'caution' ? <Icon name="warning" /> : <Icon name="info" />}{card.note.text}</p>}
      <p className="p05a-estimate-note">Donnée fictive · valeur illustrative</p>
      <button className="p05a-card-action" onClick={() => onAction()} type="button">{card.actionLabel}<span aria-hidden="true">›</span></button>
    </article>
  );
}

function PreviewPlaceholder({ destination, onBack }: { destination: Destination; onBack: () => void }) {
  const content = destination === 'plans'
    ? { title: 'Plans', text: 'La comparaison et le détail des plans arrivent dans un prochain aperçu.' }
    : destination === 'results'
      ? { title: 'Bilan', text: 'Le bilan utilisera les résultats observés et les montants confirmés.' }
      : destination === 'settings'
        ? { title: 'Réglages', text: 'La couverture et les préférences réelles seront présentées après leur intégration.' }
        : { title: 'Signaux', text: 'Retrouvez les actions fictives de cette session.' };

  return (
    <section className="p05a-placeholder">
      <p className="p05a-eyebrow">Aperçu de démonstration</p>
      <h1>{content.title}</h1>
      <p>{content.text}</p>
      <p>Données fictives · aucune action de jeu ou du Comptoir n’est exécutée.</p>
      <button className="p05a-primary-button" onClick={onBack} type="button">Retour aux Signaux</button>
    </section>
  );
}

function SessionPreferencesDrawer({
  initialPreferences,
  getAllocation,
  onApply,
  onSaveDefault,
  onClose,
}: {
  initialPreferences: SessionPreferences;
  getAllocation: (capitalPercent: number) => AllocationSummary;
  onApply: (preferences: SessionPreferences) => void;
  onSaveDefault: (preferences: SessionPreferences) => void;
  onClose: () => void;
}) {
  const [draft, setDraft] = useState(() => draftFromPreferences(initialPreferences));
  const [error, setError] = useState<string | null>(null);
  const [saveMessage, setSaveMessage] = useState<string | null>(null);
  const dialogRef = useRef<HTMLElement>(null);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;

  useEffect(() => {
    const previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    closeButtonRef.current?.focus();

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        closeRef.current();
        return;
      }
      if (event.key !== 'Tab' || dialogRef.current === null) return;
      const focusable = Array.from(dialogRef.current.querySelectorAll<HTMLElement>(
        'button:not(:disabled), input:not(:disabled), select:not(:disabled), [href], [tabindex]:not([tabindex="-1"])',
      )).filter((element) => element.getClientRects().length > 0);
      if (focusable.length === 0) {
        event.preventDefault();
        dialogRef.current.focus();
        return;
      }
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      previousFocus?.focus();
    };
  }, []);

  const update = (changes: Partial<SessionDraft>) => {
    setDraft((current) => ({ ...current, ...changes }));
    setError(null);
    setSaveMessage(null);
  };

  const apply = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const preferences = preferencesFromDraft(draft);
    if (preferences === null) {
      setError('Vérifiez la durée, le pourcentage et les montants. Les montants acceptent deux décimales au maximum.');
      return;
    }
    onApply(preferences);
    onClose();
  };

  const saveDefault = () => {
    const preferences = preferencesFromDraft(draft);
    if (preferences === null) {
      setError('Vérifiez les valeurs avant d’enregistrer cette préférence fictive.');
      return;
    }
    onSaveDefault(preferences);
    setSaveMessage('Préférence habituelle enregistrée uniquement en mémoire de démonstration.');
    setError(null);
  };

  return (
    <div className="p05a-overlay" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <section
        ref={dialogRef}
        className="p05a-drawer"
        role="dialog"
        aria-modal="true"
        aria-labelledby="p05a-drawer-title"
        aria-describedby="p05a-drawer-intro"
        tabIndex={-1}
      >
        <header className="p05a-drawer-header">
          <div>
            <span className="p05a-demo-chip p05a-demo-chip--drawer">Démonstration · données fictives</span>
            <h2 id="p05a-drawer-title">Adapter ma session</h2>
            <p id="p05a-drawer-intro">Vos choix modifient uniquement cet aperçu de démonstration.</p>
          </div>
          <button ref={closeButtonRef} className="p05a-drawer-close" onClick={onClose} type="button" aria-label="Fermer et annuler les changements">
            <span aria-hidden="true">×</span>
          </button>
        </header>

        <form className="p05a-drawer-form" onSubmit={apply} noValidate>
          <div className="p05a-drawer-scroll">
            <fieldset className="p05a-form-section p05a-objectives">
              <legend><Icon name="clock" />Objectif de la session</legend>
              <div className="p05a-objective-options">
                <button className={draft.objective === 'active_time' ? 'p05a-objective p05a-objective--selected' : 'p05a-objective'} aria-pressed={draft.objective === 'active_time'} onClick={() => update({ objective: 'active_time' })} type="button">
                  <strong>Temps pour agir</strong><span>Une durée de travail actif</span>
                </button>
                <button className={draft.objective === 'liquid_gold_deadline' ? 'p05a-objective p05a-objective--selected' : 'p05a-objective'} aria-pressed={draft.objective === 'liquid_gold_deadline'} onClick={() => update({ objective: 'liquid_gold_deadline' })} type="button">
                  <strong>Or disponible avant…</strong><span>Une échéance de liquidité</span>
                </button>
              </div>
              <label className="p05a-field-label" htmlFor="p05a-time-limit">
                {draft.objective === 'active_time' ? 'Temps actif disponible' : 'Échéance dans'}
              </label>
              <div className="p05a-duration-row" role="group" aria-label="Durée ou échéance">
                {[5, 15, 30].map((minutes) => (
                  <button key={minutes} className={Number(draft.timeLimitMinutes) === minutes ? 'p05a-choice p05a-choice--selected' : 'p05a-choice'} aria-pressed={Number(draft.timeLimitMinutes) === minutes} onClick={() => update({ timeLimitMinutes: String(minutes) })} type="button">{minutes} min</button>
                ))}
                <label className={Number(draft.timeLimitMinutes) === 5 || Number(draft.timeLimitMinutes) === 15 || Number(draft.timeLimitMinutes) === 30 ? 'p05a-custom-choice' : 'p05a-custom-choice p05a-custom-choice--selected'} htmlFor="p05a-time-limit">Personnaliser</label>
              </div>
              <input id="p05a-time-limit" aria-label={draft.objective === 'active_time' ? 'Durée personnalisée en minutes' : 'Échéance personnalisée en minutes'} min="1" step="1" type="number" value={draft.timeLimitMinutes} onChange={(event) => update({ timeLimitMinutes: event.target.value })} />
              <p className="p05a-field-help">{draft.objective === 'active_time' ? 'Le délai de vente est indiqué séparément.' : 'Une vente future à délai incertain ne devient pas de l’or disponible.'}</p>
            </fieldset>

            <fieldset className="p05a-form-section">
              <legend><Icon name="coins" />Part du capital mobilisable</legend>
              <div className="p05a-percent-row" role="group" aria-label="Pourcentage du capital">
                {[15, 30, 50].map((percent) => (
                  <button key={percent} className={Number(draft.capitalPercent) === percent ? 'p05a-choice p05a-choice--selected' : 'p05a-choice'} aria-pressed={Number(draft.capitalPercent) === percent} onClick={() => update({ capitalPercent: String(percent) })} type="button">{percent} %{percent === 30 && <small>Point de départ</small>}</button>
                ))}
                <label className={Number(draft.capitalPercent) === 15 || Number(draft.capitalPercent) === 30 || Number(draft.capitalPercent) === 50 ? 'p05a-custom-choice' : 'p05a-custom-choice p05a-custom-choice--selected'} htmlFor="p05a-capital-percent">Autre</label>
              </div>
              <input id="p05a-capital-percent" aria-label="Pourcentage personnalisé du capital" min="0" max="100" step="1" type="number" value={draft.capitalPercent} onChange={(event) => update({ capitalPercent: event.target.value })} />
              <AllocationView allocation={getAllocation(Number(draft.capitalPercent))} />
              <p className="p05a-field-help">Suggestion de départ, ajustable selon votre session. Aucun montant ne sera réservé par cet aperçu.</p>
            </fieldset>

            <fieldset className="p05a-form-section p05a-threshold-section">
              <legend><span aria-hidden="true" className="p05a-star-icon">☆</span>Ce qui vaut votre temps</legend>
              <MoneyInput id="p05a-minimum-plan" label="Gain net minimum / plan" value={draft.minimumProfitPerPlan} onChange={(value) => update({ minimumProfitPerPlan: value })} />
              <MoneyInput id="p05a-minimum-minute" label="Gain net minimum / minute active" value={draft.minimumProfitPerActiveMinute} onChange={(value) => update({ minimumProfitPerActiveMinute: value })} />
              <p className="p05a-field-help">Ce sont des seuils de sélection, pas des rendements promis.</p>
            </fieldset>

            <fieldset className="p05a-form-section">
              <legend><Icon name="plan" />Activités acceptées</legend>
              <div className="p05a-activity-grid">
                <label><input type="checkbox" checked={draft.activities.trading_post} onChange={(event) => update({ activities: { ...draft.activities, trading_post: event.target.checked } })} /><span>Comptoir</span></label>
                <label><input type="checkbox" checked={draft.activities.crafting} onChange={(event) => update({ activities: { ...draft.activities, crafting: event.target.checked } })} /><span>Artisanat</span></label>
                <label className="p05a-unsupported"><input type="checkbox" disabled /><span>Conversions</span><small>Indisponible</small></label>
                <label className="p05a-unsupported"><input type="checkbox" disabled /><span>Recyclage</span><small>Indisponible</small></label>
                <label className="p05a-unsupported"><input type="checkbox" disabled /><span>Événements et quotidiennes</span><small>Indisponible</small></label>
              </div>
            </fieldset>

            <fieldset className="p05a-form-section p05a-risk-section">
              <legend><Icon name="warning" />Risque et immobilisation</legend>
              <MoneyInput
                id="p05a-untouched-reserve"
                label="Réserve à ne pas toucher"
                value={draft.untouchedReserve}
                onChange={(value) => update({ untouchedReserve: value })}
                guidance="Cette somme reste exclue du budget suggéré. Cet aperçu ne réserve ni ne déplace votre or."
              />
              <MoneyInput
                id="p05a-downside-tolerance"
                label="Perte maximale tolérée"
                value={draft.downsideTolerance}
                onChange={(value) => update({ downsideTolerance: value })}
                guidance="Préférence fictive de risque. Aucun stop-loss n’est exécuté et ce montant ne garantit pas une perte maximale."
              />
              <label className="p05a-inline-field" htmlFor="p05a-lock-horizon">Immobilisation souhaitée (heures)
                <input id="p05a-lock-horizon" aria-describedby="p05a-lock-horizon-help" min="0" step="1" type="number" value={draft.lockHorizonHours} onChange={(event) => update({ lockHorizonHours: event.target.value })} />
              </label>
              <p className="p05a-field-help" id="p05a-lock-horizon-help">Durée souhaitée pour immobiliser le capital; cet aperçu ne garantit pas le délai d’une offre ou d’une vente.</p>
            </fieldset>
          </div>

          <div className="p05a-drawer-footer">
            {error !== null && <p className="p05a-form-error" role="alert">{error}</p>}
            {saveMessage !== null && <p className="p05a-form-success" role="status" aria-live="polite">{saveMessage}</p>}
            <div className="p05a-drawer-actions">
              <button className="p05a-primary-button" type="submit">Appliquer à cette session</button>
              <button className="p05a-save-default" onClick={saveDefault} type="button">Enregistrer comme préférence habituelle <small>(simulation en mémoire)</small></button>
              <button className="p05a-cancel-button" onClick={onClose} type="button">Annuler</button>
            </div>
            <p className="p05a-no-reservation"><Icon name="info" />Aucune ressource n’est engagée par ces réglages.</p>
          </div>
        </form>
      </section>
    </div>
  );
}

function AllocationView({ allocation }: { allocation: AllocationSummary }) {
  if (allocation.state === 'unavailable') {
    return <p className="p05a-allocation-unavailable" role="status">{allocation.message}</p>;
  }
  return (
    <div className="p05a-allocation-summary" aria-label="Résumé de capital fictif">
      <span>Base <strong>{allocation.capitalBase.label}</strong></span>
      <span>Plafond <strong>{allocation.ceiling.label}</strong></span>
      <span>Déjà engagé <strong>{allocation.committed.label}</strong></span>
      <span>Disponible <strong>{allocation.remaining.label}</strong></span>
    </div>
  );
}

function MoneyInput({ id, label, value, onChange, guidance }: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  guidance?: string;
}) {
  return (
    <>
      <label className="p05a-inline-field" htmlFor={id}>
        {label}
        <span className="p05a-money-input">
          <input id={id} inputMode="decimal" autoComplete="off" value={value} onChange={(event) => onChange(event.target.value)} aria-describedby={guidance === undefined ? id + '-help' : id + '-help ' + id + '-guidance'} />
          <span aria-hidden="true">po</span>
        </span>
        <span className="p05a-sr-only" id={id + '-help'}>Montant fictif positif ou nul, avec deux décimales au maximum.</span>
      </label>
      {guidance !== undefined && <p className="p05a-field-help" id={id + '-guidance'}>{guidance}</p>}
    </>
  );
}
