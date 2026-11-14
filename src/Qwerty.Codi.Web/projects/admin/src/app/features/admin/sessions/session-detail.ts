import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideCircleCheck,
  lucideCircleStop,
  lucideClipboardList,
  lucideMonitorPlay,
  lucidePause,
  lucidePlay,
  lucideRocket,
  lucideRotateCcw,
  lucideUsers,
} from '@ng-icons/lucide';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmDialog, HlmDialogImports } from '@spartan-ng/helm/dialog';
import { DetailHeader, type DetailCrumb } from '@shared/components/detail-header/detail-header';
import { admissionsLabel, formatInstant, sessionStatusLabel } from './session-format';
import { SessionRegiaService } from './session-regia-service';

/**
 * Regia di una sessione (§6.1, §10.4, §14.1): la pagina che comanda il ciclo di vita della classe.
 *
 * La sessione arriva dall'indirizzo (`/sessions/:sessionId`), quindi la pagina è indirizzabile e
 * ricaricabile: si rilegge dal server, non si passa dall'elenco. Ogni comando è abilitato solo
 * dallo stato in cui il server lo ammette — Avvia da «In attesa», Pausa da «In corso», Riprendi da
 * «In pausa», Stop finché non è conclusa — così il pulsante non offre un'azione che il server
 * rifiuterebbe. La chiusura chiede conferma perché è definitiva.
 *
 * Ogni comando dichiara la `expectedSessionRevision` della sessione caricata e, prima di
 * riabilitare i controlli, rilegge la sessione dal server: la revisione cambia a ogni comando e
 * l'azione successiva deve partire dallo stato confermato, non da quello ipotizzato. Un rifiuto —
 * conflitto di stato, revisione superata, versione mancante, risorsa assente — viene mostrato con
 * il suo messaggio e seguito dalla stessa rilettura (§10.7).
 *
 * La pagina comanda la **classe**: assegna un esercizio, abilita l'attività, la avvia e la riavvia.
 * Le azioni del singolo alunno stanno nella pagina dei partecipanti, dove ogni alunno è una card e
 * si vede la classe in un colpo d'occhio (§14.3); il collegamento è nell'intestazione. Lo stato è
 * condiviso con quella pagina attraverso il servizio della sessione, quindi le due pagine non hanno
 * due copie che possono divergere.
 *
 * Lo stato mostrato è quello **desiderato**: la telemetria del player non è persistita, quindi la
 * pagina non pretende di sapere che cosa il browser stia eseguendo.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButtonImports, HlmDialogImports, DetailHeader, RouterLink, NgIcon],
  providers: [
    provideIcons({
      lucideCircleCheck,
      lucideClipboardList,
      lucideMonitorPlay,
      lucidePlay,
      lucideRocket,
      lucidePause,
      lucideRotateCcw,
      lucideCircleStop,
      lucideUsers,
    }),
  ],
  selector: 'app-session-detail',
  styleUrl: './session-detail.css',
  templateUrl: './session-detail.html',
})
export class SessionDetail implements OnInit {
  /** Lo stato della sessione non è della pagina: è del servizio, condiviso con i partecipanti. */
  private readonly regia = inject(SessionRegiaService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  /** Il dialogo di conferma della chiusura: vive in un portale, lo si chiude a chiusura riuscita. */
  private readonly confirmDialog = viewChild(HlmDialog);

  protected readonly sessionId = this.regia.sessionId;
  protected readonly session = this.regia.session;
  protected readonly loading = this.regia.loading;
  protected readonly busy = this.regia.busy;
  protected readonly error = this.regia.error;
  protected readonly missingId = this.regia.missingId;
  protected readonly online = this.regia.online;
  protected readonly commands = this.regia.commands;
  protected readonly notice = this.regia.notice;

  /** Partecipanti letti per sessione: la regia li conta e sa se si può agire sulla classe. */
  protected readonly participants = this.regia.participants;

  /** Caselle della lezione, lette quando il docente apre la scelta dell'esercizio da assegnare. */
  protected readonly boxes = this.regia.boxes;

  protected readonly statusLabel = sessionStatusLabel;
  protected readonly admissions = admissionsLabel;
  protected readonly instant = formatInstant;

  /**
   * Codice di ingresso da dettare agli alunni (§9.4): si mostra solo mentre la sessione è in play —
   * avviata o in pausa —, perché la pausa sospende l'attività e non l'ingresso, mentre una sessione
   * conclusa conserva il codice nella riga ma non è più raggiungibile. Il codice non cambia con la
   * pausa, quindi il docente lo detta una volta sola all'inizio della lezione.
   */
  protected readonly joinCode = computed(() => {
    const current = this.session();
    if (current === null || (current.status !== 'Active' && current.status !== 'Paused')) {
      return null;
    }

    return current.joinCode;
  });

  /** Il codice è stato copiato: conferma l'azione senza spostare il focus dal pulsante. */
  protected readonly copied = signal(false);

  /** Copia il codice negli appunti; senza permesso il codice resta comunque leggibile a schermo. */
  protected async copyJoinCode(): Promise<void> {
    const code = this.joinCode();
    if (code === null) {
      return;
    }

    try {
      await navigator.clipboard.writeText(code);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.copied.set(false);
    }
  }

  /** Titolo della pagina: la lezione della sessione, appena è letta. */
  protected readonly title = computed(() => this.session()?.lessonTitle ?? 'Sessione');

  /** Percorso: l'elenco e la sessione che si sta conducendo. */
  protected readonly crumbs = computed<readonly DetailCrumb[]>(() => [
    { label: 'Sessioni', link: ['/sessions'] },
    { label: this.title() },
  ]);

  /** Riga di contesto: corso, classe e scuola a cui appartiene la sessione. */
  protected readonly subtitle = computed(() => {
    const current = this.session();
    if (current === null) {
      return '';
    }

    return [current.courseTitle, current.classroomName, current.schoolName].join(' · ');
  });

  /**
   * Abilitazione dei comandi per stato, letta dal servizio: sono derivate dallo stato letto dal
   * server, non da un contatore locale. Una sessione riletta come conclusa non riabilita Avvia.
   */
  protected readonly canStart = this.regia.canStart;
  protected readonly canPause = this.regia.canPause;
  protected readonly canResume = this.regia.canResume;
  protected readonly canEnd = this.regia.canEnd;
  protected readonly canChangeAdmissions = computed(
    () => this.online() && ['Active', 'Paused'].includes(this.session()?.status ?? ''),
  );

  protected async changeAdmissions(): Promise<void> {
    await this.regia.changeAdmissions();
  }

  /**
   * Comandi sul contenuto della sessione: si offrono quando la classe è in corso e c'è qualcuno a
   * cui applicarli. Le precompilazioni sono del server — §6.5 chiede che le verifichi su **tutti** i
   * destinatari prima di scrivere — quindi un rifiuto arriva qui con il suo messaggio.
   */
  protected readonly canCommandActivity = computed(
    () => this.online() && this.session()?.status === 'Active' && this.participants().length > 0,
  );

  ngOnInit(): void {
    // L'indirizzo è l'unica sorgente della sessione mostrata: la pagina si registra sul servizio,
    // che tiene una sola sottoscrizione all'hub condivisa con la pagina dei partecipanti.
    this.regia.register();
    this.destroyRef.onDestroy(() => {
      this.regia.unregister();
    });

    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(async (parameters) => {
      await this.regia.show(parameters.get('sessionId') ?? '');
    });
  }

  /** Avvia la sessione in play: ammesso solo dalla sala d'attesa. */
  protected async start(): Promise<void> {
    await this.regia.start();
  }

  /** Mette in pausa la classe: ammesso solo mentre è in corso. */
  protected async pause(): Promise<void> {
    await this.regia.pause();
  }

  /** Riprende la classe in pausa: torna in corso. */
  protected async resume(): Promise<void> {
    await this.regia.resume();
  }

  /** Conclude la sessione dopo la conferma del dialogo: è un'azione definitiva. */
  protected async confirmEnd(): Promise<void> {
    await this.regia.end();
    this.confirmDialog()?.close();
  }

  /** Abilita l'attività di tutta la classe: i controlli si accendono senza avviare il programma. */
  protected async enableClass(): Promise<void> {
    await this.regia.enableClass();
  }

  /** Avvia il progetto corrente della classe: non azzera la scena. */
  protected async runClass(): Promise<void> {
    await this.regia.runClass();
  }

  /** Riavvia la classe: la generazione avanza e il progetto resta. */
  protected async restartClass(): Promise<void> {
    await this.regia.restartClass();
  }

  /** Assegna una casella della lezione alla classe: la versione la risolve il server (§6.4). */
  protected async assign(lessonExerciseId: string): Promise<void> {
    await this.regia.assign(lessonExerciseId);
  }

  /**
   * Carica le caselle della lezione e i titoli degli esercizi all'apertura della scelta: il docente
   * sceglie da un elenco noto invece di digitare un identificativo.
   */
  protected async prepareAssignment(): Promise<void> {
    await this.regia.prepareAssignment();
  }
}
