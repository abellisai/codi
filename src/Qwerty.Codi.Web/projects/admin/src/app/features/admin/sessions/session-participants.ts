import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { provideIcons } from '@ng-icons/core';
import {
  lucideCircleCheck,
  lucideCirclePlay,
  lucideEraser,
  lucideInfo,
  lucideMessageSquare,
  lucidePause,
  lucidePlay,
  lucideRotateCcw,
  lucideUsers,
  lucidePencil,
  lucideUserX,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { SessionParticipantOverview } from '@core/api';
import { DetailHeader, type DetailCrumb } from '@shared/components/detail-header/detail-header';
import { EmptyState } from '@shared/components';
import {
  ParticipantCard,
  type ParticipantCardAction,
  type ParticipantCardActionEvent,
  type ParticipantChatSendEvent,
  type ParticipantRenameEvent,
} from './participant-card';
import { describeWork, type PreviewLine } from './preview-lines';
import { SessionRegiaService } from './session-regia-service';
import { SessionChatService } from './session-chat-service';
import { formatInstant } from './session-format';

/**
 * Partecipanti della sessione (§14.1, §14.3): le card che danno il **colpo d'occhio** sulla classe.
 *
 * La griglia a righe mostrava un alunno per riga e spostava il dettaglio sotto la tabella: così non
 * si vedeva insieme cosa sta facendo ogni banco. Qui ogni alunno è una card con nome, postazione,
 * presenza, stato desiderato, lavoro corrente e stato confermato; la card selezionata mostra il
 * proprio dettaglio e l'anteprima dei blocchi (§15.5), letta per il solo alunno aperto — non dodici
 * volte.
 *
 * I comandi di classe restano nella regia, che si raggiunge dall'intestazione: questa pagina offre
 * le azioni del singolo alunno, come pulsanti icona con tooltip ed etichetta accessibile. Le azioni
 * sono abilitate solo dallo stato che il server ammette, e ogni comando dichiara la revisione letta
 * per poi rileggere il server (§10.7).
 *
 * Lo stato appartiene al servizio condiviso con la regia: la pagina non ha una copia propria della
 * sessione, quindi passare da una pagina all'altra non mostra dati divergenti.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DetailHeader, EmptyState, RouterLink, HlmButtonImports, ParticipantCard],
  providers: [
    provideIcons({
      lucideCircleCheck,
      lucideCirclePlay,
      lucideEraser,
      lucideInfo,
      lucideMessageSquare,
      lucidePause,
      lucidePlay,
      lucideRotateCcw,
      lucideUsers,
      lucidePencil,
      lucideUserX,
    }),
  ],
  selector: 'app-session-participants',
  styleUrl: './session-participants.css',
  templateUrl: './session-participants.html',
})
export class SessionParticipants implements OnInit {
  private readonly regia = inject(SessionRegiaService);
  private readonly chat = inject(SessionChatService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  /** Stato condiviso con la regia: un solo punto che lo legge e lo aggiorna. */
  protected readonly sessionId = this.regia.sessionId;
  protected readonly session = this.regia.session;
  protected readonly participants = this.regia.participants;
  protected readonly loading = this.regia.loading;
  protected readonly busy = this.regia.busy;
  protected readonly error = this.regia.error;
  protected readonly missingId = this.regia.missingId;
  protected readonly online = this.regia.online;
  protected readonly notice = this.regia.notice;
  protected readonly detail = this.regia.detail;
  protected readonly preview = this.regia.preview;
  protected readonly previewProblem = this.regia.previewProblem;

  /** Thread della chat dell'alunno aperto: il servizio è l'unico punto che lo possiede. */
  protected readonly chatOpenFor = this.chat.openFor;
  protected readonly chatThread = this.chat.thread;
  protected readonly chatProblem = this.chat.problem;
  protected readonly chatBusy = this.chat.busy;

  /** Titolo della pagina: la lezione della sessione, appena è letta. */
  protected readonly title = computed(() => this.session()?.lessonTitle ?? 'Sessione');

  /** Percorso: elenco, sessione, partecipanti. */
  protected readonly crumbs = computed<readonly DetailCrumb[]>(() => [
    { label: 'Sessioni', link: ['/sessions'] },
    { label: this.title(), link: ['/sessions', this.sessionId()] },
    { label: 'Partecipanti' },
  ]);

  /** Riga di contesto: corso, classe e scuola a cui appartiene la sessione. */
  protected readonly subtitle = computed(() => {
    const current = this.session();
    if (current === null) {
      return '';
    }

    return [current.courseTitle, current.classroomName, current.schoolName].join(' · ');
  });

  protected readonly instant = formatInstant;

  /**
   * Card dell'alunno aperto: l'anteprima si legge per il solo alunno selezionato (§14.3). La forma
   * del lavoro la dice la versione del motore fissata dall'assegnazione.
   */
  protected readonly previewLines = computed<readonly PreviewLine[]>(() =>
    describeWork(
      this.regia.preview()?.workspaceJson ?? null,
      this.selectedAssignment()?.engineVersion ?? '',
    ),
  );

  /** Assegnazione dell'alunno aperto: da lì viene la versione del motore con cui leggere il lavoro. */
  private readonly selectedAssignment = computed(() => this.regia.detail()?.assignment ?? null);

  /** Isolare un alunno è una scelta di lettura: la card lo mostra e la pagina sa se è aperto. */
  protected isOpen(participant: SessionParticipantOverview): boolean {
    return this.regia.detailFor() === participant.participantId;
  }

  /** Chat aperta per questo alunno: la pagina lo sa, la card lo rende. */
  protected isChatOpen(participant: SessionParticipantOverview): boolean {
    return this.chat.isOpen(participant.participantId);
  }

  /** Messaggio scritto dal docente: lo invia il servizio, che è l'unico punto che lo possiede. */
  protected async sendChat(event: ParticipantChatSendEvent): Promise<void> {
    await this.chat.send(event.participant.participantId, event.text);
  }

  protected async renameParticipant(event: ParticipantRenameEvent): Promise<void> {
    await this.regia.updateParticipant(event.participant, event.displayName);
  }

  /**
   * Azioni offerte per un alunno, come pulsanti con icona ed etichetta visibile. Sono derivate
   * dallo stato letto dal server, non da un contatore locale: una sessione riletta come conclusa
   * non riabilita niente.
   */
  protected actionsFor(participant: SessionParticipantOverview): readonly ParticipantCardAction[] {
    return [
      {
        key: 'rename',
        label: 'Rinomina l’alunno',
        icon: 'lucidePencil',
        enabled: this.regia.canManageParticipant(participant),
      },
      {
        key: 'exclude',
        label: 'Escludi l’alunno',
        icon: 'lucideUserX',
        enabled: this.regia.canManageParticipant(participant),
        requiresConfirmation: true,
        confirmTitle: `Escludere ${participant.displayName}?`,
        confirmLabel: 'Escludi',
        confirmText:
          'L’alunno non può più accedere a questa sessione. Il suo lavoro resta nello storico.',
      },
      {
        key: 'enable',
        label: 'Abilita i controlli',
        icon: 'lucideCircleCheck',
        enabled: this.regia.canEnable(participant),
      },
      {
        key: 'run',
        label: 'Avvia il progetto',
        icon: 'lucidePlay',
        enabled: this.regia.canRun(participant),
      },
      {
        key: 'restart',
        label: 'Riavvia l’esecuzione',
        icon: 'lucideRotateCcw',
        enabled: this.regia.canRun(participant),
      },
      {
        key: 'pause',
        label: 'Metti in pausa l’alunno',
        icon: 'lucidePause',
        enabled: this.regia.canPauseParticipant(participant),
      },
      {
        key: 'resume',
        // Sulla pausa di recupero l'azione ha il nome della sua funzione: sbloccare senza
        // toccare il lavoro (§12.3). La ripresa da pausa docente resta l'azione abituale.
        label: participant.recoveryBlocked
          ? 'Sblocca il recupero (conserva il lavoro)'
          : 'Riprendi l’alunno',
        icon: 'lucideCirclePlay',
        enabled: this.regia.canResumeParticipant(participant),
      },
      {
        key: 'reset',
        label: 'Azzera il lavoro',
        icon: 'lucideEraser',
        enabled: this.regia.canReset(participant),
        requiresConfirmation: true,
        confirmText:
          'Il tentativo corrente viene archiviato e ne comincia uno nuovo dal modello iniziale, ' +
          'bloccato. Il lavoro precedente resta nello storico e non si riprende.',
      },
      {
        key: 'detail',
        label: 'Dettaglio dell’alunno',
        icon: 'lucideInfo',
        enabled: true,
        toggle: true,
      },
      // CHAT DISABILITATA (2026-11-14, decisione Ale): la chat maestro-alunno (§8, §15.1) è
      // sospesa in attesa di rivedere le specifiche. L'azione 'Chat con l'alunno' è commentata:
      // senza di essa la chat della card non si può aprire. Per riattivarla ripristinare questo
      // blocco insieme al caso 'chat' in `runAction` e alle bind `chat*` in
      // `session-participants.html`.
      // {
      //   key: 'chat',
      //   label: 'Chat con l’alunno',
      //   icon: 'lucideMessageSquare',
      //   enabled: this.canChat(participant),
      //   toggle: true,
      // },
    ];
  }

  /**
   * Chat ammessa: la stessa condizione che il server verifica (§8, §15.1) — una sessione non
   * conclusa e un alunno ancora ammesso. Un alunno escluso o una lezione finita non hanno più una
   * chat, perché il thread viene eliminato.
   */
  protected canChat(participant: SessionParticipantOverview): boolean {
    return this.regia.canEnd() && participant.admissionStatus === 'Admitted';
  }

  /** L'azione richiesta da una card passa al servizio, che la invia con la revisione letta. */
  protected async runAction(event: ParticipantCardActionEvent): Promise<void> {
    const participant = event.participant;

    switch (event.key) {
      case 'exclude':
        await this.regia.updateParticipant(participant);
        return;
      case 'enable':
        await this.regia.enableParticipant(participant);
        return;
      case 'run':
        await this.regia.runParticipant(participant);
        return;
      case 'restart':
        await this.regia.restartParticipant(participant);
        return;
      case 'pause':
        await this.regia.pauseParticipant(participant);
        return;
      case 'resume':
        await this.regia.resumeParticipant(participant);
        return;
      case 'reset':
        await this.regia.resetParticipant(participant);
        return;
      case 'detail':
        await this.regia.toggleDetail(participant);
        return;
      case 'chat':
        await this.chat.toggle(participant.participantId);
        return;
      default:
        // Una chiave sconosciuta non esegue nulla: la pagina non inventa un'azione.
        return;
    }
  }

  ngOnInit(): void {
    // L'indirizzo è l'unica sorgente della sessione mostrata: la sottoscrizione all'hub è condivisa
    // con la regia e si chiude quando nessuna pagina usa più la sessione.
    this.regia.register();
    this.destroyRef.onDestroy(() => {
      this.regia.unregister();
    });

    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(async (parameters) => {
      const sessionId = parameters.get('sessionId') ?? '';
      // La chat appartiene alla sessione mostrata: cambiare sessione chiude il thread aperto.
      this.chat.show(sessionId);
      await this.regia.show(sessionId);
    });
  }
}
