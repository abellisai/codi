import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { NgIcon } from '@ng-icons/core';
import { disabled, form, FormField, FormRoot, validate } from '@angular/forms/signals';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmDialog, HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmTextareaImports } from '@spartan-ng/helm/textarea';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import {
  ParticipantMessage,
  ParticipantPreviewView,
  SessionParticipantDetail,
  SessionParticipantOverview,
} from '@core/api';
import { formatInstant, presenceLabel } from './session-format';
import type { PreviewLine } from './preview-lines';

/** Limite del testo scritto dal docente: lo stesso che il dominio applica alla scrittura. */
const MaxChatGraphemes = 300;

/**
 * Conta i grafemi con il segmentatore del motore. Un emoji è un grafema per il dominio e per il
 * bambino, mentre `length` conta i code unit: una validazione basata su `length` rifiuterebbe un
 * testo che il server accetterebbe.
 */
const graphemes = new Intl.Segmenter('it', { granularity: 'grapheme' });

function graphemeCount(text: string): number {
  return [...graphemes.segment(text)].length;
}

/**
 * Card di un partecipante nella pagina dei partecipanti (§14.1, §14.3): il colpo d'occhio sulla
 * classe, senza leggere un dettaglio per alunno.
 *
 * La card mostra ciò che `ListSessionParticipants` già contiene — nome, postazione, presenza,
 * stato desiderato, lavoro corrente e stato confermato — e il collegamento al dettaglio live di
 * §14.1, dove si legge l'anteprima dei blocchi di §15.5. Il componente non decide *quali* azioni
 * sono ammesse: la pagina lo sa e gliele passa, perché l'ammissione dipende dallo stato letto dal
 * server (§10.4).
 *
 * Se la card è quella selezionata, il componente possiede il **dettaglio aperto** che la pagina gli
 * consegna: i fatti del tentativo e i blocchi letti per quell'alunno. La selezione resta evidente
 * e l'anteprima non si carica per tutte le dodici card (§14.3).
 *
 * Le azioni sono pulsanti icona con tooltip e `aria-label`: la pagina decide quali sono ammesse,
 * la card le mostra e le affida al gestore che la pagina le passa, perché l'ammissione dipende
 * dallo stato letto dal server.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgIcon,
    HlmCardImports,
    HlmButtonImports,
    HlmFieldImports,
    HlmTextareaImports,
    HlmTooltipImports,
    HlmDialogImports,
    FormField,
    FormRoot,
  ],
  selector: 'app-participant-card',
  styleUrl: './participant-card.css',
  templateUrl: './participant-card.html',
})
export class ParticipantCard {
  /** Alunno mostrato: i dati della riga, letti per sessione. */
  readonly participant = input.required<SessionParticipantOverview>();

  /** Azioni offerte: la pagina le decide, la card le rende. */
  readonly actions = input<readonly ParticipantCardAction[]>([]);

  /** Card selezionata: la pagina lo dice, la card lo rende evidente. */
  readonly isSelected = input(false);

  /** Dettaglio dell'alunno: la card lo rende solo quando è quella selezionata. */
  readonly detail = input<SessionParticipantDetail | null>(null);

  /** Anteprima dei blocchi dell'alunno (§15.5), già letta dalla pagina. */
  readonly preview = input<ParticipantPreviewView | null>(null);

  /** Righe dell'anteprima, in italiano: la card non interpreta il progetto Blockly. */
  readonly previewLines = input<readonly PreviewLine[]>([]);

  /** Errore della lettura dell'anteprima: distinto dall'assenza, che è un esito normale. */
  readonly previewProblem = input('');

  /** true mentre un comando è in corso: la card non offre un'azione doppica. */
  readonly busy = input(false);

  /** Thread dell'alunno, letto dal servizio: la card lo mostra solo quando è selezionata. */
  readonly chat = input<readonly ParticipantMessage[]>([]);

  /** Errore della chat: distinto dal thread vuoto, che è un esito normale. */
  readonly chatProblem = input('');

  /** true mentre la chat sta leggendo o scrivendo: la form non accetta un secondo invio. */
  readonly chatBusy = input(false);

  /** true quando la chat di questo alunno è quella aperta: la card lo rende evidente. */
  readonly chatOpen = input(false);

  /** Azione richiesta alla pagina, che la esegue sul server. */
  readonly action = output<ParticipantCardActionEvent>();

  /** Messaggio scritto dal docente: la pagina lo invia al server, la card non decide i permessi. */
  readonly chatSend = output<ParticipantChatSendEvent>();

  protected readonly presence = presenceLabel;
  protected readonly instant = formatInstant;

  /** Postazione: il docente riconosce il PC del bambino, quando il server la conosce. */
  protected readonly stationLabel = computed(() =>
    this.participant().stationId === null
      ? 'postazione non assegnata'
      : `postazione ${this.participant().stationId}`,
  );

  /** Lavoro corrente dell'alunno, in una riga leggibile. */
  protected readonly workLabel = computed(() => {
    const participant = this.participant();
    if (participant.attemptId === null) {
      return 'Nessun esercizio assegnato';
    }

    const attempt = `Tentativo ${participant.attemptNumber ?? '1'}`;
    const status = participant.attemptStatus ?? 'pronto';
    const revision =
      participant.projectRevision === null
        ? 'progetto iniziale'
        : `progetto rev. ${participant.projectRevision}`;

    return `${attempt} · ${status} · ${revision}`;
  });

  /** Stato confermato dall'osservazione, dichiarato separatamente da quello desiderato. */
  protected readonly observedLabel = computed(
    () => this.participant().observedState?.runtimeStatus ?? 'non disponibile',
  );

  /** Etichetta dello stato desiderato: cosa il server vuole per quell'alunno. */
  protected readonly viewLabel = computed(() => {
    const participant = this.participant();
    const parts = [participant.view ?? '—'];
    if (participant.interactionEnabled) parts.push('controlli attivi');
    if (participant.teacherPaused) parts.push('in pausa');
    if (participant.recoveryBlocked) parts.push('pausa di recupero');
    if (participant.hasDirective) parts.push('direttiva in attesa');

    return parts.join(' · ');
  });

  /** Etichetta accessibile del pulsante: l'azione e l'alunno, non solo l'azione. */
  protected labelFor(action: ParticipantCardAction): string {
    return `${action.label}: ${this.participant().displayName}`;
  }

  /** Bozza del messaggio del docente: la form la possiede, il server decide se la accetta. */
  private readonly chatModel = signal({ text: '' });

  /**
   * Form del messaggio verso l'alunno (§8, §15.1). Il limite è in grafemi, come il dominio: un emoji
   * è un grafema per il bambino e per il server, mentre `length` conta i code unit e rifiuterebbe un
   * testo che il server accetterebbe. Per questo la validazione non usa `textRules`, che conta i
   * caratteri.
   */
  protected readonly chatForm = form(
    this.chatModel,
    (path) => {
      disabled(path, () => this.chatBusy());
      validate(path.text, ({ value }) => {
        const text = value().trim();
        if (text === '') {
          return { kind: 'required', message: 'Scrivi un messaggio da inviare all’alunno.' };
        }

        return graphemeCount(text) > MaxChatGraphemes
          ? {
              kind: 'length',
              message: `Messaggio troppo lungo: al massimo ${MaxChatGraphemes} grafemi.`,
            }
          : undefined;
      });
    },
    {
      submission: {
        action: async () => {
          const text = this.chatModel().text.trim();
          this.pending.set(text);
          this.chatSend.emit({ participant: this.participant(), text });
        },
        ignoreValidators: 'none',
        onInvalid: (field) => field().errorSummary()[0]?.fieldTree().focusBoundControl(),
      },
    },
  );

  /**
   * La bozza resta quando il server rifiuta, così il docente non deve riscrivere il messaggio. Si
   * libera solo quando il messaggio confermato compare nel thread riletto dal server: la conferma
   * viene da lì, non dall'invio.
   */
  private readonly pending = signal('');

  constructor() {
    effect(() => {
      const text = this.pending();
      if (text === '') return;
      const confirmed = this.chat().some(
        (message) => message.author === 'Teacher' && message.text === text,
      );
      if (confirmed) {
        this.pending.set('');
        this.chatModel.set({ text: '' });
        this.chatForm().reset();
      }
    });
  }

  /** Autore del messaggio, in italiano: il valore del contratto è per il codice. */
  protected authorLabel(message: ParticipantMessage): string {
    return message.author === 'Teacher' ? 'Tu' : this.participant().displayName;
  }

  /** Numero di messaggi non letti dal docente, come la riga del server lo dichiara. */
  protected readonly unreadLabel = computed(() => this.participant().unreadMessages);

  /** Dialogo della conferma: vive in un portale e si chiude quando la conferma è stata data. */
  private readonly confirmDialog = viewChild(HlmDialog);

  /** Esegue l'azione sulla pagina, che sa come inviarla al server. */
  protected async run(key: string): Promise<void> {
    const action = this.actions().find((candidate) => candidate.key === key);
    this.action.emit({ participant: this.participant(), key });
    if (action?.requiresConfirmation === true) {
      await this.confirmDialog()?.close();
    }
  }
}

/** Azione di una card: il pulsante che la pagina sa eseguire, con il suo nome accessibile. */
export interface ParticipantCardAction {
  /** Identificativo stabile dell'azione, riconosciuto dalla pagina. */
  readonly key: string;
  /** Etichetta del tooltip. */
  readonly label: string;
  /** Icona Lucide registrata dalla pagina. */
  readonly icon: string;
  /** true quando lo stato letto dal server ammette l'azione. */
  readonly enabled: boolean;
  /** true quando l'azione chiede una conferma perché è definitiva. */
  readonly requiresConfirmation?: boolean;
  /** Testo della conferma: la pagina lo scrive, la card lo rende nel dialogo. */
  readonly confirmText?: string;
  /** true quando l'azione apre o chiude il dettaglio: si dichiara con `aria-pressed`. */
  readonly toggle?: boolean;
}

/** Evento richiesto dalla card: l'azione e l'alunno a cui si riferisce. */
export interface ParticipantCardActionEvent {
  readonly participant: SessionParticipantOverview;
  readonly key: string;
}

/** Messaggio da inviare: l'alunno e il testo già normalizzato dalla form della card. */
export interface ParticipantChatSendEvent {
  readonly participant: SessionParticipantOverview;
  readonly text: string;
}
