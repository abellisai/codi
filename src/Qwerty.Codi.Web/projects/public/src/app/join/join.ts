import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AvailableClassroom, AvailableSchool, PlaySession, toApiError } from '@codi/api';
import { ParticipantService } from '../core/participant-service';

/**
 * Ingresso dell'alunno (§9.4, §10.3). La via ordinaria è il **codice di ingresso**: il maestro lo
 * detta a voce, il bambino digita otto cifre e il proprio nome, e non deve conoscere né scuola né
 * classe — informazioni che a otto anni non si sanno dove prendere.
 *
 * La home **sente** se c'è una lezione in play e lo dichiara: finché nessuna lezione è avviata,
 * chiedere un codice sarebbe chiedere qualcosa che non esiste. Il controllo è periodico e si ferma
 * quando la pagina viene lasciata.
 *
 * La scelta di scuola e classe non si toglie, ma si **nasconde** dietro «Non ho il codice»: non è
 * più la via ordinaria, però un docente senza codice sotto mano o una lezione avviata prima che il
 * codice esistesse devono poter entrare lo stesso.
 *
 * **Interfaccia grezza, di proposito**: qui conta che il percorso funzioni e che cosa serve davvero
 * all'alunno di otto anni; l'aspetto si decide dopo, con il player e con i test in aula.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  selector: 'app-join',
  styleUrl: './join.css',
  templateUrl: './join.html',
})
export class Join implements OnInit {
  private readonly participants = inject(ParticipantService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Ogni quanto la home chiede se c'è una lezione in play: la stessa cadenza del battito (§11.5). */
  private static readonly PresenceIntervalMs = 3000;

  /** Lunghezza del codice di ingresso: otto cifre, come lo detta il maestro (§9.4). */
  private static readonly JoinCodeLength = 8;

  /** Nome ricordato dal browser: **non** è una credenziale, è il nome da non riscrivere ogni volta. */
  private static readonly NameStorageKey = 'codi:display-name';

  protected readonly inPlay = signal(false);
  protected readonly presenceChecked = signal(false);
  protected readonly code = signal('');
  protected readonly displayName = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  /** Il percorso con scuola e classe: si mostra solo a chi dichiara di non avere il codice. */
  protected readonly showChain = signal(false);
  protected readonly schools = signal<readonly AvailableSchool[]>([]);
  protected readonly classrooms = signal<readonly AvailableClassroom[]>([]);
  protected readonly play = signal<PlaySession | null>(null);
  protected readonly schoolId = signal('');
  protected readonly classroomId = signal('');

  /** Chiave di idempotenza corrente e il contenuto a cui appartiene (§9.4). */
  private idempotencyKey = '';
  private idempotencyPayload = '';

  private presenceTimer: ReturnType<typeof setInterval> | null = null;
  private chainLoaded = false;

  ngOnInit(): void {
    this.displayName.set(this.rememberedName());
    void this.prepare();
    this.destroyRef.onDestroy(() => this.stopPresence());
  }

  /** Bootstrap, prima sorveglianza e sorveglianza periodica della lezione in play. */
  private async prepare(): Promise<void> {
    this.busy.set(true);
    this.error.set('');

    try {
      await this.participants.bootstrap();
      await this.checkPresence();
    } catch (failure) {
      this.error.set(toApiError(failure).message);
    } finally {
      this.busy.set(false);
    }

    this.presenceTimer = setInterval(() => void this.checkPresence(), Join.PresenceIntervalMs);
  }

  /**
   * Chiede al server se c'è una lezione in play. Un guasto di rete durante la sorveglianza non
   * cancella l'esito precedente e non riempie la pagina di avvisi: la pagina resta sull'ultimo
   * stato ricevuto, e il server resta l'unico che decide se l'ingresso è possibile.
   */
  private async checkPresence(): Promise<void> {
    try {
      this.inPlay.set(await this.participants.lessonInPlay());
      this.presenceChecked.set(true);
    } catch {
      // Silenzio: la sorveglianza è un aiuto, non una condizione per entrare.
    }
  }

  private stopPresence(): void {
    if (this.presenceTimer !== null) {
      clearInterval(this.presenceTimer);
      this.presenceTimer = null;
    }
  }

  /** Il codice si digita: si tengono solo le cifre, al massimo otto. */
  protected onCode(event: Event): void {
    this.code.set(this.value(event).replace(/\D/g, '').slice(0, Join.JoinCodeLength));
  }

  protected onName(event: Event): void {
    this.displayName.set(this.value(event));
  }

  /** Si entra quando il codice ha otto cifre e c'è un nome: il resto lo decide il server. */
  protected canJoinWithCode(): boolean {
    return (
      this.code().length === Join.JoinCodeLength && this.displayName().trim() !== '' && !this.busy()
    );
  }

  /** Ingresso con il codice: è la via ordinaria dell'aula. */
  protected async joinWithCode(): Promise<void> {
    if (!this.canJoinWithCode()) {
      return;
    }

    const joinCode = this.code();
    const displayName = this.displayName().trim();

    await this.submit(
      `codice:${joinCode}:${displayName}`,
      (key) => this.participants.joinByCode({ joinCode, displayName }, key),
      displayName,
    );
  }

  /** Scopre o richiude il percorso con scuola e classe, caricandolo la prima volta che serve. */
  protected async toggleChain(): Promise<void> {
    const show = !this.showChain();
    this.showChain.set(show);

    if (!show || this.chainLoaded) {
      return;
    }

    this.chainLoaded = true;
    this.busy.set(true);
    this.error.set('');

    try {
      this.schools.set(await this.participants.schools());
    } catch (failure) {
      this.error.set(toApiError(failure).message);
      this.chainLoaded = false;
    } finally {
      this.busy.set(false);
    }
  }

  protected async chooseSchool(event: Event): Promise<void> {
    const id = this.value(event);
    this.schoolId.set(id);
    this.classroomId.set('');
    this.classrooms.set([]);
    this.play.set(null);

    if (id === '') {
      return;
    }

    this.busy.set(true);
    this.error.set('');

    try {
      this.classrooms.set(await this.participants.classrooms(id));
    } catch (failure) {
      this.error.set(toApiError(failure).message);
    } finally {
      this.busy.set(false);
    }
  }

  protected async chooseClassroom(event: Event): Promise<void> {
    const id = this.value(event);
    this.classroomId.set(id);
    this.play.set(null);

    if (id === '') {
      return;
    }

    this.busy.set(true);
    this.error.set('');

    try {
      this.play.set(await this.participants.playSession(id));
    } catch (failure) {
      this.error.set(toApiError(failure).message);
    } finally {
      this.busy.set(false);
    }
  }

  /** Si entra quando la classe ha una sessione in play e c'è un nome. */
  protected canJoinWithChain(): boolean {
    return this.play() !== null && this.displayName().trim() !== '' && !this.busy();
  }

  /** Ingresso con la catena scuola → classe → sessione: è il ripiego, non la via ordinaria. */
  protected async joinWithChain(): Promise<void> {
    const session = this.play();
    if (session === null || !this.canJoinWithChain()) {
      return;
    }

    const displayName = this.displayName().trim();

    await this.submit(
      `catena:${session.sessionId}:${displayName}`,
      (key) =>
        this.participants.join(
          {
            schoolId: session.schoolId,
            classroomId: session.classroomId,
            sessionId: session.sessionId,
            displayName,
          },
          key,
        ),
      displayName,
    );
  }

  /**
   * Esegue un tentativo di ingresso. La chiave di idempotenza **resta la stessa finché il contenuto
   * resta lo stesso** — così una risposta persa recupera la stessa partecipazione — e cambia quando
   * il contenuto cambia: dopo un codice errato il bambino ne digita un altro, e riusare la chiave
   * sarebbe un conflitto di idempotenza invece di un nuovo tentativo (§9.4).
   */
  private async submit(
    payload: string,
    send: (idempotencyKey: string) => Promise<unknown>,
    displayName: string,
  ): Promise<void> {
    this.busy.set(true);
    this.error.set('');

    try {
      await send(this.keyFor(payload));
      this.rememberName(displayName);
      await this.router.navigateByUrl('/lezione');
    } catch (failure) {
      this.error.set(toApiError(failure).message);
    } finally {
      this.busy.set(false);
    }
  }

  private keyFor(payload: string): string {
    if (this.idempotencyPayload !== payload) {
      this.idempotencyPayload = payload;
      this.idempotencyKey = this.newIdempotencyKey();
    }

    return this.idempotencyKey;
  }

  private value(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  /** Il nome dell'ultimo ingresso, se il browser lo conserva: non è una credenziale. */
  private rememberedName(): string {
    try {
      return localStorage.getItem(Join.NameStorageKey) ?? '';
    } catch {
      return '';
    }
  }

  private rememberName(displayName: string): void {
    try {
      localStorage.setItem(Join.NameStorageKey, displayName);
    } catch {
      // Un browser che non conserva nulla non impedisce di entrare: il nome si riscrive.
    }
  }

  /**
   * Chiave del tentativo di ingresso (§9.4). Non è un segreto: identifica **questo** tentativo, così
   * una risposta persa recupera la stessa partecipazione. `randomUUID` esiste solo in un contesto
   * sicuro — l'ambiente di prova non lo è sempre — quindi c'è una via di riserva.
   */
  private newIdempotencyKey(): string {
    return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
      ? crypto.randomUUID()
      : `ingresso-${Date.now()}-${Math.random().toString(16).slice(2)}`;
  }
}
